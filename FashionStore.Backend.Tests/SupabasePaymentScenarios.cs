using System.Net;
using System.Net.Http.Json;
using FashionStore.Backend.DTOs;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class SupabasePaymentScenarios
{
    internal static async Task VerifyAsync(HttpClient alice, HttpClient bob, NpgsqlConnection sql)
    {
        async Task Exec(string text) { await using var cmd = new NpgsqlCommand(text, sql); await cmd.ExecuteNonQueryAsync(); }
        async Task<T> Scalar<T>(string text) { await using var cmd = new NpgsqlCommand(text, sql); return (T)(await cmd.ExecuteScalarAsync())!; }
        async Task<string> Confirm(string id, decimal amount, string reference, string employee = "NV01")
        {
            await using var cmd = new NpgsqlCommand("SELECT webstore.confirm_bank_payment(@id,@employee,@amount,@ref)", sql);
            cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("employee", employee);
            cmd.Parameters.AddWithValue("amount", amount); cmd.Parameters.AddWithValue("ref", reference);
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        async Task<PlaceOrderResponse> Place()
        {
            Assert.Equal(HttpStatusCode.OK, (await alice.PostAsJsonAsync("/api/cart/items", new CartItemRequest { Sku = "SKU-L", Quantity = 2 })).StatusCode);
            var request = new CheckoutRequest("Alice Test", "0900000001", "123 Test street", "CHUYEN_KHOAN", "Prepaid test", Guid.NewGuid());
            var response = await alice.PostAsJsonAsync("/api/orders", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var order = (await response.Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
            var retry = await alice.PostAsJsonAsync("/api/orders", request);
            Assert.Equal(order.OrderId, (await retry.Content.ReadFromJsonAsync<PlaceOrderResponse>())!.OrderId);
            return order;
        }
        await Exec("UPDATE public.sanpham SET dongia=199000,soluongtonkho=10,soluong=10 WHERE masku='SKU-L'");
        var placed = await Place();
        Assert.Equal("CHUYEN_KHOAN", placed.PaymentMethod); Assert.Equal("MOI", placed.Status); Assert.Equal(398000, placed.Total);
        var detail = (await alice.GetFromJsonAsync<OrderDetails>("/api/orders/" + placed.OrderId))!;
        Assert.Equal("CHUA_THANH_TOAN", detail.PaymentStatus); Assert.Equal("Chờ xác nhận thanh toán", detail.ShippingStatus);
        Assert.Equal("CHUYEN_KHOAN", detail.PaymentMethod);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync("/api/orders/" + placed.OrderId)).StatusCode);
        Assert.Equal(10, await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-L'"));
        // These are the exact type sets used by the partner's two shipment queues.
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM public.hoadon WHERE upper(loaihoadon) IN ('COD','WEB_COD','GIAO_HANG','WEB_DA_THANH_TOAN','WEB_THANH_TOAN')"));
        await Assert.ThrowsAsync<PostgresException>(() => Confirm(placed.OrderId, 397999, "BANK-001"));
        await Assert.ThrowsAsync<PostgresException>(() => Confirm(placed.OrderId, 398000, "BANK-001", "MISSING"));
        await Exec("UPDATE public.sanpham SET soluongtonkho=1 WHERE masku='SKU-L'");
        await Assert.ThrowsAsync<PostgresException>(() => Confirm(placed.OrderId, 398000, "BANK-001"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM webstore.payment_confirmation"));
        await Exec("UPDATE public.sanpham SET soluongtonkho=10 WHERE masku='SKU-L'");
        async Task<string> ParallelConfirm()
        {
            await using var c = new NpgsqlConnection(sql.ConnectionString); await c.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT webstore.confirm_bank_payment(@id,'NV01',398000,'BANK-001')", c);
            cmd.Parameters.AddWithValue("id", placed.OrderId); return (string)(await cmd.ExecuteScalarAsync())!;
        }
        var confirmations = await Task.WhenAll(ParallelConfirm(), ParallelConfirm());
        Assert.Contains("CONFIRMED", confirmations); Assert.Contains("ALREADY_CONFIRMED", confirmations);
        Assert.Equal("ALREADY_CONFIRMED", await Confirm(placed.OrderId, 398000, "BANK-001"));
        await Assert.ThrowsAsync<PostgresException>(() => Confirm(placed.OrderId, 398000, "BANK-DIFFERENT"));
        Assert.Equal(8, await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-L'"));
        Assert.Equal(8, await Scalar<int>("SELECT soluong FROM public.sanpham WHERE masku='SKU-L'"));
        Assert.Equal(39, await Scalar<int>("SELECT diemtichluy FROM public.khachhang WHERE sodienthoai='0900000001'"));
        Assert.Equal(398000, await Scalar<decimal>("SELECT tongchitieu FROM public.khachhang WHERE sodienthoai='0900000001'"));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM public.hoadon WHERE upper(loaihoadon) IN ('GIAO_HANG','WEB_DA_THANH_TOAN','WEB_THANH_TOAN') AND trangthaithanhtoan='DA_THANH_TOAN' AND tiencod=0"));
        detail = (await alice.GetFromJsonAsync<OrderDetails>("/api/orders/" + placed.OrderId))!;
        Assert.Equal("DA_THANH_TOAN", detail.PaymentStatus); Assert.Equal("Vừa xác nhận đơn", detail.ShippingStatus);
        Assert.Equal("CHUYEN_KHOAN", detail.PaymentMethod);
        var summary = (await alice.GetFromJsonAsync<List<OrderSummary>>("/api/orders"))!.Single(x => x.OrderId == placed.OrderId);
        Assert.Equal("CHUYEN_KHOAN", summary.PaymentMethod);
        var second = await Place();
        await Assert.ThrowsAsync<PostgresException>(() => Confirm(second.OrderId, 398000, "BANK-001"));
        Assert.Equal(8, await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-L'"));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM webstore.payment_confirmation"));
        // Paid non-COD delivery in the partner code only changes shipping state; stock was settled above.
        await Exec($"UPDATE public.hoadon SET trangthaigiaohang='Hàng giao thành công' WHERE sohd='{placed.OrderId}'");
        Assert.Equal("HOAN_TAT", (await alice.GetFromJsonAsync<OrderDetails>("/api/orders/" + placed.OrderId))!.Status);
        Assert.Equal(8, await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-L'"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace, LATERAL aclexplode(p.proacl) a WHERE n.nspname='webstore' AND p.proname='confirm_bank_payment' AND a.grantee=0 AND a.privilege_type='EXECUTE'"));
    }
}
