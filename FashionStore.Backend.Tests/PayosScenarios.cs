using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Services;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;
internal static class PayosScenarios
{
    internal static async Task VerifyAsync(HttpClient alice,HttpClient bob,NpgsqlConnection sql,IConfiguration config)
    {
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,sql);await cmd.ExecuteNonQueryAsync();}
        async Task<T> Scalar<T>(string text){await using var cmd=new NpgsqlCommand(text,sql);return (T)(await cmd.ExecuteScalarAsync())!;}
        await Exec(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-PayOS.sql")));
        await Exec(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-PayOS.sql")));
        foreach(var pair in PayosTests.Configuration())config[pair.Key]=pair.Value;
        await Exec("UPDATE public.sanpham SET soluongtonkho=10,soluong=10 WHERE masku='SKU-M'");
        var profile=(await alice.GetFromJsonAsync<CustomerResponse>("/api/account/me"))!;
        await alice.DeleteAsync("/api/cart");await alice.PostAsJsonAsync("/api/cart/items",new CartItemRequest{Sku="SKU-M",Quantity=1});
        var placed=await alice.PostAsJsonAsync("/api/orders",new CheckoutRequest("QR Test","0900000001","Synthetic address","PAYOS",null,Guid.NewGuid()));
        Assert.True(placed.IsSuccessStatusCode,await placed.Content.ReadAsStringAsync());
        var order=(await placed.Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
        Assert.Equal(HttpStatusCode.NotFound,(await bob.PostAsync("/api/payments/"+order.OrderId+"/start",null)).StatusCode);
        var start=await alice.PostAsync("/api/payments/"+order.OrderId+"/start",null);
        Assert.True(start.IsSuccessStatusCode,await start.Content.ReadAsStringAsync());
        Assert.StartsWith("https://pay.payos.vn/web/",(await start.Content.ReadFromJsonAsync<PaymentLink>())!.Url);
        var attempt=await Scalar<string>($"SELECT id FROM webstore.gateway_attempt WHERE sohd='{order.OrderId}'");
        var beforePoints=await Scalar<int>($"SELECT diemtichluy FROM public.khachhang WHERE makh='{profile.Id}'");
        var data=new{orderCode=long.Parse(attempt),amount=order.Total,paymentLinkId=new string('a',32),code="00",currency="VND"};
        var payload=PayosTests.Envelope(data);
        var forged=payload.Replace("\"signature\":\"","\"signature\":\"bad");
        Assert.Equal(HttpStatusCode.BadRequest,(await alice.PostAsync("/api/payments/payos/webhook",new StringContent(forged,Encoding.UTF8,"application/json"))).StatusCode);
        Assert.Equal("CHUA_THANH_TOAN",await Scalar<string>($"SELECT trangthaithanhtoan FROM public.hoadon WHERE sohd='{order.OrderId}'"));
        var notifications=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>alice.PostAsync("/api/payments/payos/webhook",new StringContent(payload,Encoding.UTF8,"application/json"))));
        Assert.All(notifications,r=>Assert.True(r.IsSuccessStatusCode));
        var detail=(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+order.OrderId))!;
        Assert.Equal("DA_THANH_TOAN",detail.PaymentStatus);Assert.Equal("PAYOS",detail.PaymentMethod);
        Assert.Equal(9,await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-M'"));
        Assert.Equal(beforePoints+(int)decimal.Floor(order.Total/10000),await Scalar<int>($"SELECT diemtichluy FROM public.khachhang WHERE makh='{profile.Id}'"));
        Assert.Equal(1L,await Scalar<long>($"SELECT count(*) FROM webstore.gateway_receipt WHERE attempt_id='{attempt}'"));
        var checkedAgain=await alice.PostAsync("/api/payments/"+order.OrderId+"/recheck",null);
        Assert.Equal("PAID",(await checkedAgain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }
}
