using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FashionStore.Backend.DTOs;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class CheckoutVoucherScenarios
{
    internal static async Task VerifyAsync(HttpClient alice,NpgsqlConnection sql)
    {
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,sql);await cmd.ExecuteNonQueryAsync();}
        async Task<T> Scalar<T>(string text){await using var cmd=new NpgsqlCommand(text,sql);return (T)(await cmd.ExecuteScalarAsync())!;}
        async Task Status(HttpStatusCode expected,Task<HttpResponseMessage> request){using var response=await request;Assert.True(response.StatusCode==expected,await response.Content.ReadAsStringAsync());}
        var migration=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Checkout-Voucher-Codes.sql"));
        await Exec(migration);await Exec(migration);
        await Exec("""
            INSERT INTO public.khuyenmai(makm,tenchuongtrinh,loaikm,giatrigiam,giatridonhangtoithieu,ngaybatdau,ngayketthuc,trangthai)
            SELECT code,code,kind,value,minimum,today+starts,today+ends,active
            FROM (VALUES
                ('VOUCHER10','Phần trăm',10,0,0,0,true),
                ('TIEN20000','Số tiền',20000,0,0,1,true),
                ('MIN200','PHAN_TRAM',10,200000,0,1,true),
                ('BIGMIN','Phần trăm',10,999999,0,1,true),
                ('VOFF','Phần trăm',10,0,0,1,false),
                ('VFUTURE','Phần trăm',10,0,1,2,true),
                ('VEXPIRED','Phần trăm',10,0,-2,-1,true),
                ('VUNKNOWN','Loại chưa hỗ trợ',10,0,0,1,true),
                ('VFREE','Phần trăm',100,0,0,1,true),
                ('VTOOBIG','Số tiền',999999,0,0,1,true),
                ('DUP','Phần trăm',10,0,0,1,true),
                ('dup','Phần trăm',20,0,0,1,true)
            ) AS x(code,kind,value,minimum,starts,ends,active)
            CROSS JOIN (SELECT (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh')::date AS today) clock;
            UPDATE public.khachhang SET tongchitieu=5000001,diemtichluy=100,hangthe='Vàng' WHERE sodienthoai='0900000001';
            UPDATE public.sanpham SET dongia=200000,soluongtonkho=30,soluong=30 WHERE masku='SKU-L';
            UPDATE public.khuyenmai SET trangthai=true WHERE makm='SPKM-A';
            """);
        async Task Cart(string sku="SKU-L") {
            await Status(HttpStatusCode.OK,alice.DeleteAsync("/api/cart"));
            await Status(HttpStatusCode.OK,alice.PostAsJsonAsync("/api/cart/items",new CartItemRequest{Sku=sku,Quantity=1}));
        }
        await Cart();
        Assert.True((await alice.GetFromJsonAsync<CartResponse>("/api/cart"))!.SupportsVouchers);
        var beforeQuote=await Scalar<long>("SELECT count(*) FROM webstore.order_snapshot");
        var quote=(await alice.GetFromJsonAsync<CheckoutQuote>("/api/orders/quote?voucher=%20voucher10%20"))!;
        Assert.Equal(200000,quote.Subtotal);Assert.Equal(10000,quote.MemberDiscount);
        Assert.Equal("VOUCHER10",quote.Voucher);Assert.Equal(19000,quote.VoucherDiscount);
        Assert.Equal(29000,quote.Discount);Assert.Equal(171000,quote.Total);
        Assert.Equal(beforeQuote,await Scalar<long>("SELECT count(*) FROM webstore.order_snapshot"));
        var fixedQuote=(await alice.GetFromJsonAsync<CheckoutQuote>("/api/orders/quote?voucher=TIEN20000"))!;
        Assert.Equal(20000,fixedQuote.VoucherDiscount);Assert.Equal(170000,fixedQuote.Total);
        Assert.Equal(171000,(await alice.GetFromJsonAsync<CheckoutQuote>("/api/orders/quote?voucher=MIN200"))!.Total);
        var request=new CheckoutRequest("Voucher test","0900000001","Synthetic delivery address","COD",null,Guid.NewGuid(),"VOUCHER10");
        foreach(var invalid in new[]{"MISSING","BIGMIN","VOFF","VFUTURE","VEXPIRED","VUNKNOWN","VFREE","VTOOBIG","DUP","SPKM-A"}) {
            await Status(HttpStatusCode.Conflict,alice.GetAsync("/api/orders/quote?voucher="+invalid));
            await Status(HttpStatusCode.Conflict,alice.PostAsJsonAsync("/api/orders",request with{RequestId=Guid.NewGuid(),Voucher=invalid}));
            Assert.Equal(beforeQuote,await Scalar<long>("SELECT count(*) FROM webstore.order_snapshot"));
            Assert.Single((await alice.GetFromJsonAsync<CartResponse>("/api/cart"))!.Items);
        }
        var noCode=(await alice.GetFromJsonAsync<CheckoutQuote>("/api/orders/quote"))!;
        Assert.Null(noCode.Voucher);Assert.Equal(0,noCode.VoucherDiscount);Assert.Equal(190000,noCode.Total);
        var response=await alice.PostAsJsonAsync("/api/orders",new{request.Recipient,request.Phone,request.Address,request.PaymentMethod,request.Note,request.RequestId,
            Voucher=" voucher10 ",Total=1,Discount=199999,VoucherDiscount=199999,MemberDiscount=0});
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        var placed=(await response.Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
        Assert.Equal(171000,placed.Total);
        Assert.Equal(29000,await Scalar<decimal>($"SELECT chietkhau FROM public.hoadon WHERE sohd='{placed.OrderId}'"));
        Assert.Equal("VOUCHER10",await Scalar<string>($"SELECT makm FROM public.hoadon WHERE sohd='{placed.OrderId}'"));
        Assert.Equal(19000,await Scalar<decimal>($"SELECT voucher_discount FROM webstore.order_pricing WHERE sohd='{placed.OrderId}'"));
        await Exec("UPDATE public.khuyenmai SET giatrigiam=50,trangthai=false WHERE makm='VOUCHER10'");
        var saved=(await alice.GetFromJsonAsync<OrderDetails>("/api/orders/"+placed.OrderId))!;
        Assert.Equal(10000,saved.MemberDiscount);Assert.Equal(19000,saved.VoucherDiscount);Assert.Equal(171000,saved.Total);
        var retry=(await (await alice.PostAsJsonAsync("/api/orders",request with{Voucher="MISSING"})).Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
        Assert.Equal(placed.OrderId,retry.OrderId);Assert.Equal(171000,retry.Total);
        // A promotion disabled after quoting cannot be used to place an order.
        await Cart();
        await Exec("UPDATE public.khuyenmai SET giatrigiam=10,trangthai=true WHERE makm='VOUCHER10'");
        Assert.Equal(171000,(await alice.GetFromJsonAsync<CheckoutQuote>("/api/orders/quote?voucher=VOUCHER10"))!.Total);
        await Exec("UPDATE public.khuyenmai SET trangthai=false WHERE makm='VOUCHER10'");
        await Status(HttpStatusCode.Conflict,alice.PostAsJsonAsync("/api/orders",request with{RequestId=Guid.NewGuid()}));
        // SKU sale first, then membership, then an independent order voucher. Never reapply SPKM-A as an order code.
        await Cart("SKU-M");
        await Exec("UPDATE public.khuyenmai SET trangthai=true WHERE makm='VOUCHER10'");
        quote=(await alice.GetFromJsonAsync<CheckoutQuote>("/api/orders/quote?voucher=VOUCHER10"))!;
        Assert.Equal(150000,quote.Subtotal);Assert.Equal(7500,quote.MemberDiscount);Assert.Equal(14250,quote.VoucherDiscount);Assert.Equal(128250,quote.Total);
        var promotions=(await alice.GetFromJsonAsync<List<Dictionary<string,JsonElement>>>("/api/customer/promotions"))!;
        Assert.True(promotions.Single(p=>p["MaKM"].GetString()=="VOUCHER10")["CheckoutSupported"].GetBoolean());
        var product=promotions.Single(p=>p["MaKM"].GetString()=="SPKM-A");
        Assert.True(product["ProductPromotion"].GetBoolean());Assert.False(product["CheckoutSupported"].GetBoolean());
        // Bank settlement uses the combined discounted amount, not the undiscounted quote.
        await Cart();
        var bankResponse=await alice.PostAsJsonAsync("/api/orders",request with{RequestId=Guid.NewGuid(),PaymentMethod="CHUYEN_KHOAN"});
        Assert.True(bankResponse.IsSuccessStatusCode,await bankResponse.Content.ReadAsStringAsync());
        var bank=(await bankResponse.Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
        Assert.Equal(171000,bank.Total);
        await Assert.ThrowsAsync<PostgresException>(()=>Scalar<string>($"SELECT webstore.confirm_bank_payment('{bank.OrderId}','NV01',200000,'VOUCHER-BANK')"));
        Assert.Equal("CONFIRMED",await Scalar<string>($"SELECT webstore.confirm_bank_payment('{bank.OrderId}','NV01',171000,'VOUCHER-BANK')"));
        // Leave the rest of the shared integration fixture's native-sale state unchanged.
        await Exec("UPDATE public.khuyenmai SET trangthai=false WHERE makm='SPKM-A'");
    }
}
