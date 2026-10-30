using System.Net;
using System.Net.Http.Json;
using FashionStore.Backend.DTOs;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class OrderRefundScenarios
{
    internal static async Task VerifyAsync(HttpClient alice,HttpClient bob,NpgsqlConnection sql)
    {
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,sql);await cmd.ExecuteNonQueryAsync();}
        async Task<T> Scalar<T>(string text){await using var cmd=new NpgsqlCommand(text,sql);return (T)(await cmd.ExecuteScalarAsync())!;}
        var migration=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Order-Cancellation-Refunds.sql"));
        await Exec(migration);await Exec(migration);
        var customer=(await alice.GetFromJsonAsync<CustomerResponse>("/api/account/me"))!;
        // Each paid order crosses the Bạc/Vàng threshold; cancelling must restore the original rank too.
        await Exec($"UPDATE public.khachhang SET tongchitieu=4999000,diemtichluy=100,hangthe='Bạc' WHERE makh='{customer.Id}'");
        async Task<(int Stock,int Quantity,int Points,decimal Spend,string Rank)> Balance()=> (
            await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-M'"),
            await Scalar<int>("SELECT soluong FROM public.sanpham WHERE masku='SKU-M'"),
            await Scalar<int>($"SELECT diemtichluy FROM public.khachhang WHERE makh='{customer.Id}'"),
            await Scalar<decimal>($"SELECT tongchitieu FROM public.khachhang WHERE makh='{customer.Id}'"),
            await Scalar<string>($"SELECT hangthe FROM public.khachhang WHERE makh='{customer.Id}'"));
        async Task<PlaceOrderResponse> Place(string method)
        {
            await Exec("UPDATE public.sanpham SET soluongtonkho=20,soluong=20,dongia=200000 WHERE masku='SKU-M'");
            await alice.DeleteAsync("/api/cart");
            Assert.Equal(HttpStatusCode.OK,(await alice.PostAsJsonAsync("/api/cart/items",new CartItemRequest{Sku="SKU-M",Quantity=1})).StatusCode);
            var response=await alice.PostAsJsonAsync("/api/orders",new CheckoutRequest("Refund test customer","0900000001","Synthetic delivery address",method,null,Guid.NewGuid()));
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
        }
        async Task Settle(PlaceOrderResponse order)
        {
            if(order.PaymentMethod=="CHUYEN_KHOAN") {
                Assert.Equal("CONFIRMED",await Scalar<string>($"SELECT webstore.confirm_bank_payment('{order.OrderId}','NV01',{order.Total.ToString(System.Globalization.CultureInfo.InvariantCulture)},'RF-{Guid.NewGuid():N}')"));
                return;
            }
            Assert.True((await alice.PostAsync("/api/payments/"+order.OrderId+"/start",null)).IsSuccessStatusCode);
            Assert.Equal("PAID",await Scalar<string>($"SELECT webstore.settle_gateway_payment(id,provider,merchant,amount,'RF-'||id) FROM webstore.gateway_attempt WHERE sohd='{order.OrderId}' ORDER BY created_at DESC LIMIT 1"));
        }
        var input=new CancelOrderRequest("Đặt nhầm sản phẩm, muốn chọn mẫu khác","SYNTHETIC CUSTOMER","1234567890","Ngân hàng kiểm thử");
        string? previousRefundReference=null;
        foreach(var method in new[]{"CHUYEN_KHOAN","PAYOS","VISA","VNPAY","ZALOPAY"}) {
            var order=await Place(method);var id=order.OrderId;var before=await Balance();
            Assert.Equal("MOI",order.Status);
            // No reason, or a direct call to the old SQL signature, must not bypass the new form.
            Assert.Equal(HttpStatusCode.BadRequest,(await alice.PostAsync("/api/orders/"+id+"/cancel",null)).StatusCode);
            var reasonError=await Assert.ThrowsAsync<PostgresException>(()=>Scalar<string>($"SELECT webstore.cancel_customer_order('{customer.Id}','{id}')"));
            Assert.Equal("22023",reasonError.SqlState);
            await Settle(order);
            var paid=await Balance();Assert.Equal(before.Stock-1,paid.Stock);
            Assert.Equal("Vàng",paid.Rank);
            Assert.True((await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+id))!.CanCancel);
            Assert.Equal(HttpStatusCode.BadRequest,(await alice.PostAsJsonAsync("/api/orders/"+id+"/cancel",new CancelOrderRequest(input.Reason))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await alice.PostAsJsonAsync("/api/orders/"+id+"/cancel",input with{AccountNumber="invalid"})).StatusCode);
            Assert.Equal(paid,await Balance());
            Assert.Equal(HttpStatusCode.NotFound,(await bob.PostAsJsonAsync("/api/orders/"+id+"/cancel",input)).StatusCode);
            var responses=await Task.WhenAll(alice.PostAsJsonAsync("/api/orders/"+id+"/cancel",input),alice.PostAsJsonAsync("/api/orders/"+id+"/cancel",input));
            Assert.All(responses,r=>Assert.Equal(HttpStatusCode.NoContent,r.StatusCode));
            Assert.Equal(before,await Balance()); // Restore stock AND reverse only this order's earned loyalty once.
            var detail=(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+id))!;
            Assert.Equal("HUY",detail.Status);Assert.False(detail.CanCancel);Assert.Equal(input.Reason,detail.CancellationReason);
            Assert.Equal("DA_THANH_TOAN",detail.PaymentStatus);Assert.NotNull(detail.Refund);
            Assert.Equal("CHO_HOAN_TIEN",detail.Refund.Status);Assert.Equal(order.Total,detail.Refund.Amount);
            Assert.Equal("•••• 7890",detail.Refund.MaskedAccountNumber);Assert.Null(detail.Refund.RefundedAt);
            Assert.DoesNotContain(input.AccountNumber!,await alice.GetStringAsync("/api/orders/"+id));
            Assert.Equal(1L,await Scalar<long>($"SELECT count(*) FROM webstore.order_refund WHERE sohd='{id}'"));
            Assert.True(await Scalar<bool>($"SELECT stock_restored FROM webstore.order_cancellation WHERE sohd='{id}'"));
            await Assert.ThrowsAsync<PostgresException>(()=>Scalar<string>($"SELECT webstore.complete_customer_refund('{id}','MISSING','REFUND-TEST')"));
            if(previousRefundReference!=null) {
                var duplicate=await Assert.ThrowsAsync<PostgresException>(()=>Scalar<string>($"SELECT webstore.complete_customer_refund('{id}','NV01','{previousRefundReference}')"));
                Assert.Equal("23505",duplicate.SqlState);
                Assert.Equal("CHO_HOAN_TIEN",(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+id))!.Refund!.Status);
            }
            Assert.Equal("REFUNDED",await Scalar<string>($"SELECT webstore.complete_customer_refund('{id}','NV01','REFUND-{id}')"));
            previousRefundReference="REFUND-"+id;
            Assert.Equal("ALREADY_REFUNDED",await Scalar<string>($"SELECT webstore.complete_customer_refund('{id}','NV01','REFUND-{id}')"));
            await Assert.ThrowsAsync<PostgresException>(()=>Scalar<string>($"SELECT webstore.complete_customer_refund('{id}','NV01','DIFFERENT-REFERENCE')"));
            detail=(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+id))!;
            Assert.Equal("HOAN_TIEN",detail.PaymentStatus);Assert.Equal("DA_HOAN_TIEN",detail.Refund!.Status);
            Assert.Equal(DateTimeKind.Utc,detail.Refund.RefundedAt!.Value.Kind);Assert.Equal(before,await Balance());
        }
        // Paid orders still cannot cancel after preparation / carrier collection starts.
        var blocked=await Place("PAYOS");await Settle(blocked);
        foreach(var shipping in new[]{"DANG_CHUAN_BI","Chờ đơn vị vận chuyển tới lấy hàng","Shipper đã lấy hàng","Hàng đang được vận chuyển","Hàng giao thành công"}) {
            await Exec($"UPDATE public.hoadon SET trangthaigiaohang='{shipping}' WHERE sohd='{blocked.OrderId}'");var paid=await Balance();
            Assert.False((await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+blocked.OrderId))!.CanCancel);
            Assert.Equal(HttpStatusCode.Conflict,(await alice.PostAsJsonAsync("/api/orders/"+blocked.OrderId+"/cancel",input)).StatusCode);
            Assert.Equal(paid,await Balance());
        }
        // The earlier race test produced a verified late payment with no inventory/loyalty effects.
        var late=await Scalar<string>($"SELECT sohd FROM webstore.order_refund WHERE makh='{customer.Id}' AND status='CHO_THONG_TIN' ORDER BY createdat LIMIT 1");
        var lateBalance=await Balance();
        Assert.Equal(HttpStatusCode.NotFound,(await bob.PostAsJsonAsync("/api/orders/"+late+"/refund-account",input)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await alice.PostAsJsonAsync("/api/orders/"+late+"/refund-account",new CancelOrderRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await alice.PostAsJsonAsync("/api/orders/"+late+"/refund-account",input)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await alice.PostAsJsonAsync("/api/orders/"+late+"/refund-account",input with{AccountNumber="9876543210"})).StatusCode);
        Assert.Equal("CHO_HOAN_TIEN",(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+late))!.Refund!.Status);
        Assert.Equal(lateBalance,await Balance());
        // COD starts pending; an admin's existing WinForms status update confirms it.
        var cod=await Place("COD");var codDetail=(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+cod.OrderId))!;
        Assert.Equal("MOI",codDetail.Status);Assert.Equal("Chờ xác nhận",codDetail.ShippingStatus);
        await Exec($"UPDATE public.hoadon SET trangthaigiaohang='Vừa xác nhận đơn' WHERE sohd='{cod.OrderId}'");
        Assert.Equal("XAC_NHAN",(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+cod.OrderId))!.Status);
        Assert.Equal(HttpStatusCode.NoContent,(await alice.PostAsync("/api/orders/"+cod.OrderId+"/cancel",null)).StatusCode);
        Assert.Null((await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+cod.OrderId))!.Refund);
    }
}
