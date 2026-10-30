using System.Net.Http.Json;
using FashionStore.Backend.DTOs;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class NativePromotionScenarios
{
    internal static async Task VerifyAsync(HttpClient client,NpgsqlConnection sql)
    {
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,sql);await cmd.ExecuteNonQueryAsync();}
        await Exec("""
            CREATE TABLE public.khuyenmaisanpham(makm varchar(20) REFERENCES public.khuyenmai,masku varchar(20) REFERENCES public.sanpham,phantramgiam numeric(5,2),PRIMARY KEY(makm,masku));
            UPDATE public.sanpham SET dongia=200000,soluongtonkho=10,soluong=10 WHERE masku='SKU-M';
            UPDATE public.sanpham SET dongia=300000,soluongtonkho=10,soluong=10 WHERE masku='SKU-L';
            UPDATE public.khuyenmai SET trangthai=false;
            INSERT INTO public.khuyenmai VALUES
                ('SPKM-A','SKU sale','Phần trăm',25,0,CURRENT_DATE-1,CURRENT_DATE+2,true),
                ('SPKM-B','Overlapping','Phần trăm',10,0,CURRENT_DATE-1,CURRENT_DATE+2,true),
                ('SPKM-OLD','Expired','Phần trăm',90,0,CURRENT_DATE-4,CURRENT_DATE-2,true),
                ('SPKM-OFF','Disabled','Phần trăm',80,0,CURRENT_DATE-1,CURRENT_DATE+2,false),
                ('SPKM-MIN','Conditional','Phần trăm',95,1000000,CURRENT_DATE-1,CURRENT_DATE+2,true);
            INSERT INTO public.khuyenmaisanpham VALUES ('SPKM-A','SKU-M',25),('SPKM-B','SKU-M',10),('SPKM-OLD','SKU-M',90),('SPKM-OFF','SKU-M',80),('SPKM-MIN','SKU-M',95);
            """);
        var migration=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Product-Promotions.sql"));
        await Exec(migration);await Exec(migration);
        var detail=(await client.GetFromJsonAsync<ProductDetailResponse>("/api/products/P1"))!;
        Assert.Equal(150000,detail.Price);Assert.Equal(200000,detail.OriginalPrice);
        Assert.Equal(150000,detail.Variants.Single(v=>v.Sku=="SKU-M").Price);
        Assert.Null(detail.Variants.Single(v=>v.Sku=="SKU-L").OriginalPrice);
        var sale=(await client.GetFromJsonAsync<ProductListResponse>("/api/products?saleOnly=true&sort=price_asc&pageSize=100"))!;
        Assert.Equal("P1",Assert.Single(sale.Items).Id);Assert.Equal(150000,sale.Items[0].Price);
        Assert.Empty((await client.GetFromJsonAsync<ProductListResponse>("/api/products?saleOnly=true&minPrice=150001"))!.Items);
        await client.DeleteAsync("/api/cart");await client.PostAsJsonAsync("/api/cart/items",new CartItemRequest{Sku="SKU-M",Quantity=2});
        Assert.Equal(300000,(await client.GetFromJsonAsync<CartResponse>("/api/cart"))!.Subtotal);
        var quote=(await client.GetFromJsonAsync<CheckoutQuote>("/api/orders/quote"))!;Assert.Equal(300000,quote.Subtotal);
        var placed=await client.PostAsJsonAsync("/api/orders",new CheckoutRequest("Test customer","0900000001","Synthetic delivery address","COD",null,Guid.NewGuid()));
        Assert.True(placed.IsSuccessStatusCode,await placed.Content.ReadAsStringAsync());
        var id=(await placed.Content.ReadFromJsonAsync<PlaceOrderResponse>())!.OrderId;
        await Exec("UPDATE public.khuyenmai SET trangthai=false WHERE makm IN ('SPKM-A','SPKM-B')");
        Assert.DoesNotContain((await client.GetFromJsonAsync<ProductListResponse>("/api/products?saleOnly=true"))!.Items,p=>p.Id=="P1");
        Assert.Equal(200000,(await client.GetFromJsonAsync<ProductDetailResponse>("/api/products/P1"))!.Price);
        Assert.Equal(150000,Assert.Single((await client.GetFromJsonAsync<OrderDetails>("/api/orders/"+id))!.Items).UnitPrice);
        // The bridge never writes/compounds public product prices.
        await using var basePrice=new NpgsqlCommand("SELECT dongia FROM public.sanpham WHERE masku='SKU-M'",sql);
        Assert.Equal(200000m,(decimal)(await basePrice.ExecuteScalarAsync())!);
    }
}
