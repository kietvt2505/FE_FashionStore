using System.Net;
using System.Net.Http.Json;
using FashionStore.Backend.DTOs;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class SupabaseCheckoutPricingScenarios
{
    internal static async Task VerifyAsync(HttpClient alice, NpgsqlConnection sql)
    {
        async Task Exec(string text) { await using var cmd=new NpgsqlCommand(text,sql); await cmd.ExecuteNonQueryAsync(); }
        async Task<T> Scalar<T>(string text) { await using var cmd=new NpgsqlCommand(text,sql); return (T)(await cmd.ExecuteScalarAsync())!; }
        async Task Status(HttpStatusCode expected,Task<HttpResponseMessage> request) {
            using var response=await request;
            Assert.True(response.StatusCode==expected,$"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
        Assert.False((await alice.GetFromJsonAsync<CartResponse>("/api/cart"))!.SupportsVouchers);
        await Exec(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Checkout-Promotions.sql")));
        var previous=await Scalar<string>("SELECT sohd FROM webstore.payment_confirmation WHERE reference='BANK-001'");
        await Exec($"INSERT INTO webstore.order_pricing(sohd,subtotal,discount,voucher) VALUES('{previous}',398000,0,'LEGACY')");
        await Exec("UPDATE public.sanpham SET dongia=100000,soluongtonkho=20,soluong=20,trangthai=true WHERE masku='SKU-L'; UPDATE public.khachhang SET tongchitieu=5000001,diemtichluy=321,hangthe='Bạc' WHERE sodienthoai='0900000001'");
        await Status(HttpStatusCode.OK,alice.DeleteAsync("/api/cart"));
        await Status(HttpStatusCode.OK,alice.PostAsJsonAsync("/api/cart/items",new CartItemRequest {Sku="SKU-L",Quantity=2}));
        var request=new CheckoutRequest("Alice Membership","0900000001","Synthetic delivery address","COD",null,Guid.NewGuid());
        var count=await Scalar<long>("SELECT count(*) FROM public.hoadon");
        // Missing migration must not silently remove an eligible customer's discount.
        await Status(HttpStatusCode.ServiceUnavailable,alice.GetAsync("/api/orders/quote"));
        await Status(HttpStatusCode.ServiceUnavailable,alice.PostAsJsonAsync("/api/orders",request));
        Assert.Equal(count,await Scalar<long>("SELECT count(*) FROM public.hoadon"));
        var migration=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Membership-Pricing.sql"));
        await Exec(migration);await Exec(migration);
        Assert.False((await alice.GetFromJsonAsync<CartResponse>("/api/cart"))!.SupportsVouchers);
        var old=(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+previous))!;
        Assert.Equal(398000,old.Total);Assert.Equal("LEGACY",old.Voucher);Assert.Null(old.MemberRank);
        // Spending thresholds, not stale rank text or points, determine the current discount.
        foreach(var tier in new[]{(0m,0,"Bạc"),(5000000m,0,"Bạc"),(5000001m,5,"Vàng"),(15000000m,5,"Vàng"),(15000001m,10,"Kim Cương")}) {
            await Exec($"UPDATE public.khachhang SET tongchitieu={tier.Item1},diemtichluy=321 WHERE sodienthoai='0900000001'");
            var quote=(await alice.GetFromJsonAsync<CheckoutQuote>("/api/orders/quote"))!;
            Assert.True(quote.MembershipEnabled);Assert.Equal(tier.Item3,quote.MemberRank);
            Assert.Equal(tier.Item2,quote.MemberRate);Assert.Equal(321,quote.MemberPoints);
            Assert.Equal(200000*tier.Item2/100,quote.Discount);Assert.Equal(200000-quote.Discount,quote.Total);
        }
        await Exec("UPDATE public.khachhang SET tongchitieu=5000001 WHERE sodienthoai='0900000001'; UPDATE public.sanpham SET dongia=100005 WHERE masku='SKU-L'");
        Assert.Equal(10001,(await alice.GetFromJsonAsync<CheckoutQuote>("/api/orders/quote"))!.Discount);
        await Exec("UPDATE public.sanpham SET dongia=100000 WHERE masku='SKU-L'");
        await Status(HttpStatusCode.ServiceUnavailable,alice.GetAsync("/api/orders/quote?voucher=ACTIVE"));
        await Status(HttpStatusCode.ServiceUnavailable,alice.PostAsJsonAsync("/api/orders",request with {Voucher="ACTIVE"}));
        // Forged money, rank and points fields cannot affect the transaction.
        var response=await alice.PostAsJsonAsync("/api/orders",new {request.Recipient,request.Phone,request.Address,request.PaymentMethod,request.Note,request.RequestId,Total=1,Discount=999999,MemberRate=100,MemberPoints=999999});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var placed=(await response.Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
        Assert.Equal(190000,placed.Total);
        var detail=(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+placed.OrderId))!;
        Assert.Equal(200000,detail.Subtotal);Assert.Equal(10000,detail.Discount);Assert.Null(detail.Voucher);
        Assert.Equal("Vàng",detail.MemberRank);Assert.Equal(5,detail.MemberRate);Assert.Equal(321,detail.MemberPoints);Assert.Equal(10000,detail.MemberDiscount);
        Assert.Equal(200000,detail.Items.Sum(i=>i.Total));
        Assert.Equal(200000,await Scalar<decimal>($"SELECT tongcong FROM public.hoadon WHERE sohd='{placed.OrderId}'"));
        Assert.Equal(10000,await Scalar<decimal>($"SELECT chietkhau FROM public.hoadon WHERE sohd='{placed.OrderId}'"));
        Assert.Equal(190000,await Scalar<decimal>($"SELECT tiencod FROM public.hoadon WHERE sohd='{placed.OrderId}'"));
        Assert.True(await Scalar<bool>($"SELECT makm IS NULL FROM public.hoadon WHERE sohd='{placed.OrderId}'"));
        Assert.Equal(20,await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-L'"));
        Assert.Equal(321,await Scalar<int>("SELECT diemtichluy FROM public.khachhang WHERE sodienthoai='0900000001'"));
        await Exec("UPDATE public.khachhang SET tongchitieu=16000000,diemtichluy=777 WHERE sodienthoai='0900000001'; UPDATE public.sanpham SET dongia=150000 WHERE masku='SKU-L'");
        var retry=await alice.PostAsJsonAsync("/api/orders",request);
        Assert.Equal(placed.OrderId,(await retry.Content.ReadFromJsonAsync<PlaceOrderResponse>())!.OrderId);
        detail=(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+placed.OrderId))!;
        Assert.Equal(190000,detail.Total);Assert.Equal("Vàng",detail.MemberRank);Assert.Equal(321,detail.MemberPoints);
        await Exec("UPDATE public.sanpham SET dongia=100000 WHERE masku='SKU-L'; UPDATE public.khachhang SET tongchitieu=5000001 WHERE sodienthoai='0900000001'");
        await Status(HttpStatusCode.OK,alice.PostAsJsonAsync("/api/cart/items",new CartItemRequest {Sku="SKU-L",Quantity=1}));
        Assert.Equal(95000,(await alice.GetFromJsonAsync<CheckoutQuote>("/api/orders/quote"))!.Total);
        // Placement rechecks a rank that improved after the quote.
        await Exec("UPDATE public.khachhang SET tongchitieu=16000000 WHERE sodienthoai='0900000001'");
        var bankResponse=await alice.PostAsJsonAsync("/api/orders",request with {RequestId=Guid.NewGuid(),PaymentMethod="CHUYEN_KHOAN"});
        Assert.Equal(HttpStatusCode.OK,bankResponse.StatusCode);
        var bank=(await bankResponse.Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
        Assert.Equal(90000,bank.Total);
        await Assert.ThrowsAsync<PostgresException>(()=>Scalar<string>($"SELECT webstore.confirm_bank_payment('{bank.OrderId}','NV01',100000,'MEMBER-BANK-001')"));
        Assert.Equal("CONFIRMED",await Scalar<string>($"SELECT webstore.confirm_bank_payment('{bank.OrderId}','NV01',90000,'MEMBER-BANK-001')"));
        Assert.Equal("ALREADY_CONFIRMED",await Scalar<string>($"SELECT webstore.confirm_bank_payment('{bank.OrderId}','NV01',90000,'MEMBER-BANK-001')"));
        Assert.Equal(19,await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-L'"));
        Assert.Equal(786,await Scalar<int>("SELECT diemtichluy FROM public.khachhang WHERE sodienthoai='0900000001'"));
        Assert.Equal(16090000,await Scalar<decimal>("SELECT tongchitieu FROM public.khachhang WHERE sodienthoai='0900000001'"));
        await Exec(migration);
        Assert.Equal(10000,(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+bank.OrderId))!.MemberDiscount);
    }
}
