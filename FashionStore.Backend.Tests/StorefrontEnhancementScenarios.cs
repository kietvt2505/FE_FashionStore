using System.Net;
using System.Net.Http.Json;
using FashionStore.Backend.DTOs;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class StorefrontEnhancementScenarios
{
    internal static async Task VerifyAsync(HttpClient alice,HttpClient bob,NpgsqlConnection sql)
    {
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,sql);await cmd.ExecuteNonQueryAsync();}
        async Task<T> Scalar<T>(string text){await using var cmd=new NpgsqlCommand(text,sql);return (T)(await cmd.ExecuteScalarAsync())!;}
        async Task<CartResponse> Cart(HttpClient owner)=>(await owner.GetFromJsonAsync<CartResponse>("/api/cart"))!;
        async Task Status(HttpStatusCode expected,Task<HttpResponseMessage> task){using var response=await task;Assert.True(response.StatusCode==expected,await response.Content.ReadAsStringAsync());}
        await Status(HttpStatusCode.ServiceUnavailable,alice.PostAsJsonAsync("/api/cart/merge",new GuestCartMergeRequest{RequestId=Guid.NewGuid(),Items=[new(){Sku="SKU-L",Quantity=1}]}));
        var migration=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Storefront-Enhancements.sql"));
        await Exec(migration);await Exec(migration);
        await Exec("DROP FUNCTION webstore.merge_guest_cart(varchar,uuid,jsonb)");
        using(var missing=await alice.PostAsJsonAsync("/api/cart/merge",new GuestCartMergeRequest{RequestId=Guid.NewGuid(),Items=[new(){Sku="SKU-L",Quantity=1}]})){
            Assert.Equal(HttpStatusCode.ServiceUnavailable,missing.StatusCode);
            Assert.Contains("Sản phẩm bạn đã chọn vẫn được giữ lại",await missing.Content.ReadAsStringAsync());
        }
        await Exec(migration);
        await Exec("""
            UPDATE public.sanpham SET dongia=100000,soluongtonkho=5,soluong=5,trangthai=true WHERE masku='SKU-M';
            UPDATE public.sanpham SET dongia=200000,soluongtonkho=5,soluong=5,trangthai=true WHERE masku='SKU-L';
            SELECT webstore.set_product_discount('P1',15);
            SELECT webstore.set_product_discount('P1',15);
            """);
        Assert.Equal(85000,await Scalar<decimal>("SELECT dongia FROM public.sanpham WHERE masku='SKU-M'"));
        Assert.Equal(170000,await Scalar<decimal>("SELECT dongia FROM public.sanpham WHERE masku='SKU-L'"));
        var sale=(await alice.GetFromJsonAsync<ProductListResponse>("/api/products?saleOnly=true&pageSize=100"))!;
        Assert.Equal(100000,sale.Items.Single(p=>p.Id=="P1").OriginalPrice);
        Assert.Equal(85000,sale.Items.Single(p=>p.Id=="P1").Price);
        var discounted=(await alice.GetFromJsonAsync<ProductDetailResponse>("/api/products/P1"))!;
        Assert.Equal(200000,discounted.Variants.Single(v=>v.Sku=="SKU-L").OriginalPrice);
        await Exec("SELECT webstore.set_product_discount('P1',20)");
        Assert.Equal(80000,await Scalar<decimal>("SELECT dongia FROM public.sanpham WHERE masku='SKU-M'"));
        await Exec("SELECT webstore.set_product_discount('P1',0)");
        Assert.Equal(200000,await Scalar<decimal>("SELECT dongia FROM public.sanpham WHERE masku='SKU-L'"));
        Assert.True(await Scalar<bool>("SELECT regular_price IS NULL FROM webstore.catalog_product WHERE masp='P1'"));
        // A new base price entered at POS must not reuse an obsolete base price.
        await Exec("UPDATE public.sanpham SET dongia=250000 WHERE masku='SKU-L';SELECT webstore.set_product_discount('P1',10)");
        Assert.Equal(225000,await Scalar<decimal>("SELECT dongia FROM public.sanpham WHERE masku='SKU-L'"));
        await Exec("SELECT webstore.set_product_discount('P1',0)");
        Assert.Equal(250000,await Scalar<decimal>("SELECT dongia FROM public.sanpham WHERE masku='SKU-L'"));
        await Assert.ThrowsAsync<PostgresException>(()=>Exec("SELECT webstore.set_product_discount('P1',100)"));
        Assert.Equal(250000,await Scalar<decimal>("SELECT dongia FROM public.sanpham WHERE masku='SKU-L'"));
        await Exec("""
            UPDATE webstore.catalog_product SET description='Mô tả từ cơ sở dữ liệu <không thực thi HTML>' WHERE masp='P1';
            INSERT INTO webstore.product_media(masp,color,url,alt,sort_order) VALUES
              ('P1','Đỏ','https://example.test/red-back.png','Mặt sau màu đỏ',1),
              ('P1','Đỏ','https://example.test/red-side.png','Góc nghiêng màu đỏ',2),
              ('P1','Đỏ','https://example.test/red-side.png','Ảnh trùng',3);
            """);
        var detail=(await alice.GetFromJsonAsync<ProductDetailResponse>("/api/products/P1"))!;
        Assert.Equal("Mô tả từ cơ sở dữ liệu <không thực thi HTML>",detail.Description);
        Assert.Equal(2,detail.Images.Count(i=>i.Color=="Đỏ" && i.Url.StartsWith("https://example.test/")));
        Assert.Contains(detail.Images,i=>i.Alt=="Mặt sau màu đỏ");
        var filters=(await alice.GetFromJsonAsync<ProductListResponse>("/api/products?q=P1&size=M&color=Đen"))!;
        Assert.Equal(new[]{"L","M"},filters.Sizes);Assert.Equal(new[]{"Đen","Đỏ"},filters.Colors);
        Assert.Single(filters.Items);
        await Status(HttpStatusCode.OK,alice.DeleteAsync("/api/cart"));await Status(HttpStatusCode.OK,bob.DeleteAsync("/api/cart"));
        await Status(HttpStatusCode.OK,alice.PostAsJsonAsync("/api/cart/items",new CartItemRequest{Sku="SKU-L",Quantity=1}));
        var merge=new GuestCartMergeRequest{RequestId=Guid.NewGuid(),Items=[new(){Sku="SKU-L",Quantity=1},new(){Sku="SKU-M",Quantity=2}]};
        await Status(HttpStatusCode.OK,alice.PostAsJsonAsync("/api/cart/merge",merge));
        await Status(HttpStatusCode.OK,alice.PostAsJsonAsync("/api/cart/merge",merge));
        Assert.Equal(4,(await Cart(alice)).TotalQuantity);
        Assert.Equal(700000,(await Cart(alice)).Subtotal);
        Assert.Empty((await Cart(bob)).Items);
        // Same request ID in a different account is isolated, and does not disclose another cart.
        await Status(HttpStatusCode.OK,bob.PostAsJsonAsync("/api/cart/merge",merge));
        Assert.Equal(3,(await Cart(bob)).TotalQuantity);
        Assert.Equal(4,(await Cart(alice)).TotalQuantity);
        var failed=new GuestCartMergeRequest{RequestId=Guid.NewGuid(),Items=[new(){Sku="SKU-L",Quantity=1},new(){Sku="SKU-M",Quantity=6}]};
        await Status(HttpStatusCode.Conflict,alice.PostAsJsonAsync("/api/cart/merge",failed));
        Assert.Equal(4,(await Cart(alice)).TotalQuantity);
        failed.Items[1].Quantity=1;
        await Status(HttpStatusCode.OK,alice.PostAsJsonAsync("/api/cart/merge",failed));
        Assert.Equal(6,(await Cart(alice)).TotalQuantity);
        var concurrent=new GuestCartMergeRequest{RequestId=Guid.NewGuid(),Items=[new(){Sku="SKU-M",Quantity=1}]};
        var duplicates=await Task.WhenAll(alice.PostAsJsonAsync("/api/cart/merge",concurrent),alice.PostAsJsonAsync("/api/cart/merge",concurrent));
        Assert.All(duplicates,r=>Assert.Equal(HttpStatusCode.OK,r.StatusCode));
        Assert.Equal(7,(await Cart(alice)).TotalQuantity);
        await Status(HttpStatusCode.BadRequest,alice.PostAsJsonAsync("/api/cart/merge",new GuestCartMergeRequest{RequestId=Guid.Empty,Items=merge.Items}));
        await Status(HttpStatusCode.BadRequest,alice.PostAsJsonAsync("/api/cart/merge",new{RequestId=Guid.NewGuid(),Items=new[]{new{Sku="SKU-M",Quantity=-1}}}));
        var profile=(await alice.GetFromJsonAsync<CustomerResponse>("/api/account/me"))!;
        await Assert.ThrowsAsync<PostgresException>(()=>Exec($"SELECT webstore.merge_guest_cart('{profile.Id}','{Guid.NewGuid()}',NULL)"));
        await Assert.ThrowsAsync<PostgresException>(()=>Exec($"SELECT webstore.merge_guest_cart('{profile.Id}','{Guid.NewGuid()}', '[{{\"sku\":\"SKU-M\",\"quantity\":-1}},{{\"sku\":\"SKU-M\",\"quantity\":2}}]'::jsonb)"));
        // Declared but unconfigured providers cannot create invoices, even with a forged browser request.
        var count=await Scalar<long>("SELECT count(*) FROM public.hoadon");
        foreach(var method in new[]{"VISA","ZALOPAY","VNPAY"})
            await Status(HttpStatusCode.Conflict,alice.PostAsJsonAsync("/api/orders",new CheckoutRequest("Alice","0900000001","Synthetic delivery address",method,null,Guid.NewGuid())));
        Assert.Equal(count,await Scalar<long>("SELECT count(*) FROM public.hoadon"));
    }
}
