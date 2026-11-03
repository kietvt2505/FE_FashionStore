using System.Net.Http.Json;
using FashionStore.Backend.DTOs;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class NativeProductImageScenarios
{
    internal static async Task VerifyAsync(HttpClient client,NpgsqlConnection sql)
    {
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,sql);await cmd.ExecuteNonQueryAsync();}
        async Task<ProductDetailResponse> Detail() => (await client.GetFromJsonAsync<ProductDetailResponse>("/api/products/NATIVE-P1"))!;
        await Exec("""
            INSERT INTO public.sanpham(masku,masp,maloai,tensanpham,kichco,mausac,dongia,soluongtonkho,trangthai,hinhanh) VALUES
              ('NI-BLK-M','NATIVE-P1','AO','Áo nhiều góc','M','Đen',100000,3,true,'https://example.test/native-black-front.png'),
              ('NI-BLK-L','NATIVE-P1','AO','Áo nhiều góc','L','Đen',100000,4,true,'https://example.test/native-black-front.png'),
              ('NI-RED-M','NATIVE-P1','AO','Áo nhiều góc','M','Đỏ',120000,2,true,'https://example.test/native-red-front.png'),
              ('NI-HIDDEN','NATIVE-P1','AO','Áo nhiều góc','XL','Đen',100000,9,false,'/hidden.png');
            """);
        // Covers still work without the optional JSON column in older partner databases.
        var legacy=await Detail();
        Assert.Equal(2,legacy.Images.Count);
        await Exec("""
            ALTER TABLE public.sanpham ADD COLUMN danhsachhinhanh jsonb NOT NULL DEFAULT '[]'::jsonb;
            UPDATE public.sanpham SET danhsachhinhanh=
              '["https://example.test/native-black-front.png","https://example.test/native-black-side.png","https://example.test/native-black-back.png","https://example.test/native-black-front.png"]'::jsonb
              WHERE masku IN ('NI-BLK-M','NI-BLK-L');
            UPDATE public.sanpham SET danhsachhinhanh=
              '["https://example.test/native-red-front.png"," https://example.test/native-red-side.png ",null,42,{"url":"/invalid.png"},""]'::jsonb WHERE masku='NI-RED-M';
            UPDATE public.sanpham SET danhsachhinhanh='["/hidden-back.png"]'::jsonb WHERE masku='NI-HIDDEN';
            """);
        var gallery=await Detail();
        Assert.Equal(new[]{"https://example.test/native-black-front.png","https://example.test/native-black-side.png","https://example.test/native-black-back.png"},
            gallery.Images.Where(i=>i.Color=="Đen").Select(i=>i.Url));
        Assert.Equal(new[]{"https://example.test/native-red-front.png","https://example.test/native-red-side.png"},gallery.Images.Where(i=>i.Color=="Đỏ").Select(i=>i.Url));
        Assert.DoesNotContain(gallery.Images,i=>i.Sku=="NI-HIDDEN");
        Assert.Equal(legacy.Price,gallery.Price);Assert.Equal(legacy.Stock,gallery.Stock);
        // Native galleries and website media stay additive, with duplicate URLs removed.
        await Exec("""
            INSERT INTO public.hinhanhsanpham(masp,color,url,alt,sort_order) VALUES
              ('NATIVE-P1','Đen','https://example.test/native-black-side.png','Góc nghiêng',1),
              ('NATIVE-P1','Đen','https://example.test/native-black-detail.png','Cận chất liệu',2);
            UPDATE public.sanpham SET hinhanh=NULL WHERE masku IN ('NI-BLK-M','NI-BLK-L');
            """);
        var card=(await client.GetFromJsonAsync<ProductListResponse>("/api/products?q=NATIVE-P1"))!.Items.Single();
        Assert.Equal("https://example.test/native-black-front.png",card.ColorOptions.Single(c=>c.Color=="Đen").ImageUrl);
        Assert.Equal(4,(await Detail()).Images.Count(i=>i.Color=="Đen"));
        // Non-array values cannot break the catalog or leak another color's pictures.
        await Exec("UPDATE public.sanpham SET danhsachhinhanh='{\"bad\":true}'::jsonb WHERE masku='NI-RED-M'");
        Assert.Equal("https://example.test/native-red-front.png",Assert.Single((await Detail()).Images,i=>i.Color=="Đỏ").Url);
    }
}
