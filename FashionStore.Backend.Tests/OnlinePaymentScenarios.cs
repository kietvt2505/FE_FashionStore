using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Services;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class OnlinePaymentScenarios
{
    internal static async Task VerifyAsync(HttpClient alice,HttpClient bob,NpgsqlConnection sql,IConfiguration config)
    {
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,sql);await cmd.ExecuteNonQueryAsync();}
        async Task<T> Scalar<T>(string text){await using var cmd=new NpgsqlCommand(text,sql);return (T)(await cmd.ExecuteScalarAsync())!;}
        var migration=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Online-Payments.sql"));
        await Exec(migration);await Exec(migration);
        Assert.All((await alice.GetFromJsonAsync<List<OnlinePaymentOption>>("/api/payments/options"))!,p=>Assert.False(p.Enabled));
        foreach(var pair in PaymentGatewayTests.Configuration())config[pair.Key]=pair.Value;
        Assert.All((await alice.GetFromJsonAsync<List<OnlinePaymentOption>>("/api/payments/options"))!.Where(p=>p.Code!="PAYOS"),p=>Assert.True(p.Enabled));
        var profile=(await alice.GetFromJsonAsync<CustomerResponse>("/api/account/me"))!;
        await Exec($"UPDATE public.khachhang SET tongchitieu=0,diemtichluy=0 WHERE makh='{profile.Id}'");
        async Task<(string Order,string Attempt)> Place(string method)
        {
            await Exec("UPDATE public.sanpham SET soluongtonkho=10,soluong=10 WHERE masku='SKU-M'");
            await alice.DeleteAsync("/api/cart");await alice.PostAsJsonAsync("/api/cart/items",new CartItemRequest{Sku="SKU-M",Quantity=1});
            var response=await alice.PostAsJsonAsync("/api/orders",new CheckoutRequest("Test customer","0900000001","Synthetic delivery address",method,null,Guid.NewGuid()));
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
            var order=(await response.Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
            Assert.Equal(HttpStatusCode.NotFound,(await bob.PostAsync("/api/payments/"+order.OrderId+"/start",null)).StatusCode);
            var start=await alice.PostAsJsonAsync("/api/payments/"+order.OrderId+"/start",new {ClientIp="203.0.113.7"});
            Assert.True(start.IsSuccessStatusCode,await start.Content.ReadAsStringAsync());
            var link=(await start.Content.ReadFromJsonAsync<PaymentLink>())!;
            if(method!="ZALOPAY")Assert.Contains("vnp_IpAddr=203.0.113.7",link.Url);
            var repeat=(await (await alice.PostAsync("/api/payments/"+order.OrderId+"/start",null)).Content.ReadFromJsonAsync<PaymentLink>())!;
            Assert.Equal(link.Url,repeat.Url);
            Assert.Equal(1L,await Scalar<long>($"SELECT count(*) FROM webstore.gateway_attempt WHERE sohd='{order.OrderId}'"));
            return (order.OrderId,await Scalar<string>($"SELECT id FROM webstore.gateway_attempt WHERE sohd='{order.OrderId}'"));
        }
        string Vnp(string attempt,long transaction,decimal amount=200000)
        {
            var data=new Dictionary<string,string> {
                ["vnp_TmnCode"]="TESTONLY",["vnp_TxnRef"]=attempt,["vnp_Amount"]=(amount*100).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["vnp_ResponseCode"]="00",["vnp_TransactionStatus"]="00",["vnp_TransactionNo"]=transaction.ToString()
            };
            var query=PaymentGatewayClient.VnpData(data);
            return query+"&vnp_SecureHash="+PaymentGatewayClient.Hmac("synthetic-vnp-secret-only",query,true);
        }
        long transactionId=10000;
        foreach(var method in new[]{"VISA","VNPAY","ZALOPAY"})
        {
            var (order,attempt)=await Place(method);transactionId++;
            var beforeStock=await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-M'");
            var beforePoints=await Scalar<int>($"SELECT diemtichluy FROM public.khachhang WHERE makh='{profile.Id}'");
            Func<Task<HttpResponseMessage>> notify;
            if(method!="ZALOPAY") {
                var data=Vnp(attempt,transactionId);
                var browser=await alice.GetAsync("/api/payments/vnpay/return?"+data);
                Assert.Equal(HttpStatusCode.Redirect,browser.StatusCode);
                Assert.Equal("CHUA_THANH_TOAN",await Scalar<string>($"SELECT trangthaithanhtoan FROM public.hoadon WHERE sohd='{order}'"));
                var bad=await alice.GetFromJsonAsync<JsonElement>("/api/payments/vnpay/ipn?"+data.Replace("vnp_Amount=20000000","vnp_Amount=10000"));
                Assert.Equal("97",bad.GetProperty("RspCode").GetString());
                Assert.True(bad.TryGetProperty("Message",out _));
                Assert.False(bad.TryGetProperty("rspCode",out _));
                var mismatch=await alice.GetFromJsonAsync<JsonElement>("/api/payments/vnpay/ipn?"+Vnp(attempt,transactionId,1));
                Assert.Equal("04",mismatch.GetProperty("RspCode").GetString());
                notify=()=>alice.GetAsync("/api/payments/vnpay/ipn?"+data);
            }else {
                var pending=await alice.PostAsync("/api/payments/"+order+"/recheck",null);
                Assert.Equal("PENDING",(await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
                var data=JsonSerializer.Serialize(new{app_id=1234,app_trans_id=attempt,amount=200000,zp_trans_id=transactionId});
                var mac=PaymentGatewayClient.Hmac("synthetic-zalo-key2-only",data);
                var forged=await alice.PostAsJsonAsync("/api/payments/zalopay/callback",new{data,mac="bad",type=1});
                Assert.Equal(-1,(await forged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("return_code").GetInt32());
                notify=()=>alice.PostAsJsonAsync("/api/payments/zalopay/callback",new{data,mac,type=1});
            }
            var replies=await Task.WhenAll(notify(),notify());
            foreach(var reply in replies) {
                Assert.True(reply.IsSuccessStatusCode,await reply.Content.ReadAsStringAsync());
                var body=await reply.Content.ReadFromJsonAsync<JsonElement>();
                if(method!="ZALOPAY")Assert.Equal("00",body.GetProperty("RspCode").GetString());
                else Assert.Equal(1,body.GetProperty("return_code").GetInt32());
            }
            Assert.Equal("DA_THANH_TOAN",await Scalar<string>($"SELECT trangthaithanhtoan FROM public.hoadon WHERE sohd='{order}'"));
            Assert.Equal("GIAO_HANG",await Scalar<string>($"SELECT loaihoadon FROM public.hoadon WHERE sohd='{order}'"));
            Assert.Equal(beforeStock-1,await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-M'"));
            Assert.Equal(beforePoints+20,await Scalar<int>($"SELECT diemtichluy FROM public.khachhang WHERE makh='{profile.Id}'"));
            Assert.Equal(HttpStatusCode.Conflict,(await alice.PostAsync("/api/payments/"+order+"/start",null)).StatusCode);
            var checkedAgain=await alice.PostAsync("/api/payments/"+order+"/recheck",null);
            Assert.Equal("PAID",(await checkedAgain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.NotFound,(await bob.PostAsync("/api/payments/"+order+"/recheck",null)).StatusCode);
        }
        // Money received after stock changed: retain proof, never oversell or silently lose the payment.
        var review=await Place("VNPAY");
        await Exec("UPDATE public.sanpham SET soluongtonkho=0,soluong=0 WHERE masku='SKU-M'");
        await alice.GetAsync("/api/payments/vnpay/ipn?"+Vnp(review.Attempt,11000));
        Assert.Equal("REVIEW",await Scalar<string>($"SELECT status FROM webstore.gateway_attempt WHERE id='{review.Attempt}'"));
        Assert.Equal(0,await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-M'"));
        Assert.Equal("CAN_HO_TRO",(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+review.Order))!.Status);
    }
}
