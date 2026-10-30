using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Services;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class OrderCancellationScenarios
{
    internal static async Task VerifyAsync(HttpClient alice,HttpClient bob,NpgsqlConnection sql)
    {
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,sql);await cmd.ExecuteNonQueryAsync();}
        async Task<T> Scalar<T>(string text){await using var cmd=new NpgsqlCommand(text,sql);return (T)(await cmd.ExecuteScalarAsync())!;}
        var migration=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Customer-Order-Cancellation.sql"));
        await Exec(migration);await Exec(migration);
        var policyMigration=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Order-Cancellation-Policy.sql"));
        await Exec(policyMigration);await Exec(policyMigration);
        var refundMigration=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Order-Cancellation-Refunds.sql"));
        await Exec(refundMigration);await Exec(refundMigration);
        var profile=(await alice.GetFromJsonAsync<CustomerResponse>("/api/account/me"))!;
        async Task<(PlaceOrderResponse Order,CheckoutRequest Request)> Place(string method)
        {
            await Exec("UPDATE public.sanpham SET soluong=10,soluongtonkho=10 WHERE masku='SKU-M'");
            await alice.DeleteAsync("/api/cart");
            Assert.Equal(HttpStatusCode.OK,(await alice.PostAsJsonAsync("/api/cart/items",new CartItemRequest{Sku="SKU-M",Quantity=1})).StatusCode);
            var request=new CheckoutRequest("Synthetic Customer","0900000001","Synthetic delivery address",method,null,Guid.NewGuid());
            var response=await alice.PostAsJsonAsync("/api/orders",request);
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
            return ((await response.Content.ReadFromJsonAsync<PlaceOrderResponse>())!,request);
        }
        async Task<(int Stock,int Quantity,int Points,decimal Spend)> Balance()=> (
            await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-M'"),
            await Scalar<int>("SELECT soluong FROM public.sanpham WHERE masku='SKU-M'"),
            await Scalar<int>($"SELECT diemtichluy FROM public.khachhang WHERE makh='{profile.Id}'"),
            await Scalar<decimal>($"SELECT tongchitieu FROM public.khachhang WHERE makh='{profile.Id}'"));
        foreach(var method in new[]{"COD","CHUYEN_KHOAN"}) {
            var placed=await Place(method);var id=placed.Order.OrderId;
            var before=await Balance();
            Assert.True((await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+id))!.CanCancel);
            Assert.Equal(HttpStatusCode.NotFound,(await bob.PostAsync("/api/orders/"+id+"/cancel",null)).StatusCode);
            var cancel=()=>alice.PostAsJsonAsync("/api/orders/"+id+"/cancel",new CancelOrderRequest("Không còn nhu cầu mua sản phẩm"));
            var responses=await Task.WhenAll(cancel(),cancel());
            Assert.All(responses,r=>Assert.Equal(HttpStatusCode.NoContent,r.StatusCode));
            var detail=(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+id))!;
            Assert.Equal("HUY",detail.Status);Assert.Equal("Đã hủy",detail.ShippingStatus);
            Assert.False(detail.CanCancel);Assert.NotNull(detail.CancelledAt);Assert.Equal(DateTimeKind.Utc,detail.CancelledAt.Value.Kind);
            Assert.Single(detail.Items);Assert.Equal(before,await Balance());
            Assert.Equal(1L,await Scalar<long>($"SELECT count(*) FROM webstore.order_cancellation WHERE sohd='{id}'"));
            Assert.Equal("WEB_HUY",await Scalar<string>($"SELECT loaihoadon FROM public.hoadon WHERE sohd='{id}'"));
            Assert.Equal(0L,await Scalar<long>($"SELECT count(*) FROM public.hoadon WHERE sohd='{id}' AND loaihoadon IN ('COD','WEB_COD','GIAO_HANG','WEB_DA_THANH_TOAN','WEB_THANH_TOAN')"));
            Assert.Contains((await alice.GetFromJsonAsync<List<OrderSummary>>("/api/orders?status=HUY"))!,o=>o.OrderId==id);
            var retry=(await (await alice.PostAsJsonAsync("/api/orders",placed.Request)).Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
            Assert.Equal(id,retry.OrderId);Assert.Equal("HUY",retry.Status);
            // These exact late operations are possible after the old WinForms reads a pending invoice.
            await Assert.ThrowsAsync<PostgresException>(()=>Exec($"UPDATE public.hoadon SET trangthaigiaohang='Shipper đã lấy hàng' WHERE sohd='{id}'"));
            await Assert.ThrowsAsync<PostgresException>(()=>Exec($"DELETE FROM public.hoadon WHERE sohd='{id}'"));
        }
        // Both milestone names and existing database aliases allow pending and confirmed orders only.
        foreach(var shipping in new[]{"MOI","CHO_XAC_NHAN","CHUA_GIAO","Chờ xác nhận","Chờ xác nhận thanh toán","XAC_NHAN","Đã xác nhận","Vừa xác nhận đơn"}) {
            var placed=await Place("COD");var id=placed.Order.OrderId;
            await Exec($"UPDATE public.hoadon SET trangthaigiaohang='{shipping}' WHERE sohd='{id}'");
            Assert.True(await Scalar<bool>($"SELECT webstore.can_cancel_order('{profile.Id}','{id}')"));
            Assert.True((await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+id))!.CanCancel);
            Assert.Equal(HttpStatusCode.NoContent,(await alice.PostAsync("/api/orders/"+id+"/cancel",null)).StatusCode);
        }
        foreach(var shipping in new[]{"DANG_CHUAN_BI","Đang chuẩn bị","CHO_LAY_HANG","Chờ lấy hàng",
            "Chờ đơn vị vận chuyển tới lấy hàng","Chờ đơn vị vận chuyển lấy hàng","DA_LAY_HANG","Shipper đã lấy hàng",
            "Đơn vị vận chuyển lấy hàng thành công","DANG_GIAO","Đang giao hàng","Hàng đang được vận chuyển",
            "DA_GIAO","Đã giao","Hàng giao thành công","Trạng thái chưa ánh xạ",""}) {
            var placed=await Place("COD");var id=placed.Order.OrderId;
            await Exec($"UPDATE public.hoadon SET trangthaigiaohang='{shipping}' WHERE sohd='{id}'");
            var before=await Balance();
            Assert.False(await Scalar<bool>($"SELECT webstore.can_cancel_order('{profile.Id}','{id}')"));
            Assert.False((await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+id))!.CanCancel);
            Assert.Equal(HttpStatusCode.Conflict,(await alice.PostAsync("/api/orders/"+id+"/cancel",null)).StatusCode);
            var rejection=await Assert.ThrowsAsync<PostgresException>(()=>Scalar<string>($"SELECT webstore.cancel_customer_order('{profile.Id}','{id}')"));
            Assert.Equal("P0001",rejection.SqlState);
            Assert.Equal(shipping,await Scalar<string>($"SELECT trangthaigiaohang FROM public.hoadon WHERE sohd='{id}'"));
            Assert.Equal(0L,await Scalar<long>($"SELECT count(*) FROM webstore.order_cancellation WHERE sohd='{id}'"));
            Assert.Equal(before,await Balance());
        }
        // Protect the API during rollout, even if Supabase still has the old, broader function.
        var oldPolicy=policyMigration.Replace("'XAC_NHAN','Đã xác nhận','Vừa xác nhận đơn')", "'XAC_NHAN','Đã xác nhận','Vừa xác nhận đơn','DANG_CHUAN_BI')");
        Assert.NotEqual(policyMigration,oldPolicy);
        await Exec(oldPolicy);
        var preparing=await Place("COD");var preparingId=preparing.Order.OrderId;
        await Exec($"UPDATE public.hoadon SET trangthaigiaohang='DANG_CHUAN_BI' WHERE sohd='{preparingId}'");
        Assert.True(await Scalar<bool>($"SELECT webstore.can_cancel_order('{profile.Id}','{preparingId}')"));
        Assert.False((await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+preparingId))!.CanCancel);
        Assert.Equal(HttpStatusCode.Conflict,(await alice.PostAsync("/api/orders/"+preparingId+"/cancel",null)).StatusCode);
        await Exec(refundMigration);
        var paid=await Place("CHUYEN_KHOAN");
        await Exec($"UPDATE public.hoadon SET loaihoadon='GIAO_HANG',trangthaithanhtoan='DA_THANH_TOAN',tienkhachdua=khachphaitra,trangthaigiaohang='Vừa xác nhận đơn' WHERE sohd='{paid.Order.OrderId}'");
        Assert.False((await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+paid.Order.OrderId))!.CanCancel);
        Assert.Equal(HttpStatusCode.Conflict,(await alice.PostAsync("/api/orders/"+paid.Order.OrderId+"/cancel",null)).StatusCode);
        // Confirmation/cancellation serialize: cancel first refuses settlement; settle first cancels with one refund request.
        var race=await Place("CHUYEN_KHOAN");var raceId=race.Order.OrderId;var raceBefore=await Balance();
        async Task<bool> Confirm()
        {
            await using var connection=new NpgsqlConnection(sql.ConnectionString);await connection.OpenAsync();
            await using var cmd=new NpgsqlCommand("SELECT webstore.confirm_bank_payment(@id,'NV01',@amount,'CANCEL-RACE')",connection);
            cmd.Parameters.AddWithValue("id",raceId);cmd.Parameters.AddWithValue("amount",race.Order.Total);
            try {await cmd.ExecuteScalarAsync();return true;}catch(PostgresException e) when(e.SqlState=="P0001"){return false;}
        }
        var confirming=Confirm();var cancelling=alice.PostAsJsonAsync("/api/orders/"+raceId+"/cancel",new CancelOrderRequest("Không còn nhu cầu mua","SYNTHETIC CUSTOMER","1234567890","Ngân hàng thử nghiệm"));
        await Task.WhenAll(confirming,cancelling);
        var raceDetail=(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+raceId))!;
        Assert.False(raceDetail.CanCancel);
        Assert.Equal(HttpStatusCode.NoContent,(await cancelling).StatusCode);Assert.Equal("HUY",raceDetail.Status);Assert.Equal(raceBefore,await Balance());
        if(await confirming) {Assert.NotNull(raceDetail.Refund);Assert.Equal("CHO_HOAN_TIEN",raceDetail.Refund.Status);}
        else Assert.Null(raceDetail.Refund);
        // A signed payment arriving after cancellation is retained for staff review, not fulfilled or silently discarded.
        var late=await Place("VNPAY");var lateId=late.Order.OrderId;
        Assert.True((await alice.PostAsync("/api/payments/"+lateId+"/start",null)).IsSuccessStatusCode);
        var attempt=await Scalar<string>($"SELECT id FROM webstore.gateway_attempt WHERE sohd='{lateId}'");
        Assert.Equal(HttpStatusCode.NoContent,(await alice.PostAsJsonAsync("/api/orders/"+lateId+"/cancel",new CancelOrderRequest("Không còn nhu cầu mua"))).StatusCode);
        var beforeLate=await Balance();
        var data=new Dictionary<string,string>{["vnp_TmnCode"]="TESTONLY",["vnp_TxnRef"]=attempt,["vnp_Amount"]=decimal.ToInt64(late.Order.Total*100).ToString(System.Globalization.CultureInfo.InvariantCulture),["vnp_ResponseCode"]="00",["vnp_TransactionStatus"]="00",["vnp_TransactionNo"]="910001"};
        var query=PaymentGatewayClient.VnpData(data);
        var result=await alice.GetFromJsonAsync<JsonElement>("/api/payments/vnpay/ipn?"+query+"&vnp_SecureHash="+PaymentGatewayClient.Hmac("synthetic-vnp-secret-only",query,true));
        Assert.Equal("00",result.GetProperty("RspCode").GetString());
        var lateDetail=(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+lateId))!;
        Assert.Equal("CAN_HO_TRO",lateDetail.Status);Assert.Equal("DA_THANH_TOAN",lateDetail.PaymentStatus);
        Assert.False(lateDetail.CanCancel);Assert.NotNull(lateDetail.CancelledAt);Assert.Equal(beforeLate,await Balance());
        Assert.Equal("REVIEW",await Scalar<string>($"SELECT outcome FROM webstore.gateway_receipt WHERE attempt_id='{attempt}'"));
        // The guard must not suppress the partner's normal deletion of OTHER pending COD invoices.
        var removable=await Place("COD");
        await Exec($"DELETE FROM public.chitiethoadon WHERE sohd='{removable.Order.OrderId}'; DELETE FROM public.hoadon WHERE sohd='{removable.Order.OrderId}'");
        Assert.Equal("CAN_HO_TRO",(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+removable.Order.OrderId))!.Status);
    }
}
