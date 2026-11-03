using System.Net.Http.Json;
using FashionStore.Backend.DTOs;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class NativeProductDescriptionScenarios
{
    internal static async Task VerifyAsync(HttpClient client,NpgsqlConnection sql)
    {
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,sql);await cmd.ExecuteNonQueryAsync();}
        async Task<ProductDetailResponse> Detail() => (await client.GetFromJsonAsync<ProductDetailResponse>("/api/products/DESC-P1"))!;
        await Exec("""
            INSERT INTO public.sanpham(masku,masp,maloai,tensanpham,kichco,mausac,dongia,soluongtonkho,trangthai) VALUES
              ('ND-A','DESC-P1','AO','Áo có mô tả','M','Đen',100000,3,true),
              ('ND-B','DESC-P1','AO','Áo có mô tả','L','Đen',100000,4,true),
              ('ND-HIDDEN','DESC-P1','AO','Áo có mô tả','XL','Đen',100000,9,false);
            INSERT INTO public.thongtinsanpham(masp,description) VALUES('DESC-P1','Mô tả website đã lưu.');
            """);
        // Old schemas continue serving the existing description before the additive migration.
        var legacy=await Detail();
        Assert.Equal("Mô tả website đã lưu.",legacy.Description);
        var migration=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Product-Descriptions.sql"));
        await Exec(migration);await Exec(migration);
        Assert.Equal(legacy.Description,(await Detail()).Description);
        await Exec("""
            UPDATE public.sanpham SET mota='   ' WHERE masku='ND-A';
            UPDATE public.sanpham SET mota='Mô tả riêng từ dữ liệu sản phẩm <không thực thi HTML>.' WHERE masku='ND-B';
            UPDATE public.sanpham SET mota='Không được lộ mô tả SKU đã ẩn.' WHERE masku='ND-HIDDEN';
            """);
        var described=await Detail();
        Assert.Equal("Mô tả riêng từ dữ liệu sản phẩm <không thực thi HTML>.",described.Description);
        Assert.Equal(legacy.Price,described.Price);Assert.Equal(legacy.Stock,described.Stock);
        await Exec("UPDATE public.sanpham SET mota=NULL WHERE masku='ND-B'");
        Assert.Equal(legacy.Description,(await Detail()).Description);
        await Exec("UPDATE public.thongtinsanpham SET description=NULL WHERE masp='DESC-P1'");
        Assert.True(string.IsNullOrWhiteSpace((await Detail()).Description));
    }
}
