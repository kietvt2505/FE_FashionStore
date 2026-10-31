using System.Net.Http.Json;
using FashionStore.Backend.DTOs;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class CatalogClassificationScenarios
{
    internal static async Task VerifyAsync(HttpClient client,NpgsqlConnection sql)
    {
        await using(var seed=new NpgsqlCommand("""
            INSERT INTO public.loaisanpham VALUES
              ('NEW-TM','Áo thun nam',true),('NEW-PM','Quần tây nam',true),('NEW-HM','Áo hoodie nam',true),
              ('NEW-TF','Áo thun baby tee nữ',true),('NEW-DF','Đầm',true),('NEW-UF','Đồ lót nữ',true),
              ('NEW-HAT','Nón',true),('NEW-SOCK','Vớ',true);
            INSERT INTO public.sanpham(masku,masp,maloai,tensanpham,kichco,mausac,dongia,soluongtonkho,trangthai) VALUES
              ('CUR-TM','CUR-TM','NEW-TM','Áo thun nam','M','Đen',100,3,true),
              ('CUR-PM','CUR-PM','NEW-PM','Quần tây nam','M','Đen',200,4,true),
              ('CUR-HM','CUR-HM','NEW-HM','Áo hoodie nam','M','Đen',300,5,true),
              ('CUR-TF','CUR-TF','NEW-TF','Baby tee nữ','M','Đỏ',400,6,true),
              ('CUR-DF','CUR-DF','NEW-DF','Đầm nữ','M','Đỏ',500,7,true),
              ('CUR-UF','CUR-UF','NEW-UF','Áo lót nữ','M','Đỏ',600,8,true),
              ('CUR-HAT','CUR-HAT','NEW-HAT','Nón','M','Đen',700,9,true),
              ('CUR-SOCK','CUR-SOCK','NEW-SOCK','Vớ','M','Đen',800,10,true);
            -- Deliberately stale assignments reproduce the partner replacing the old product IDs.
            INSERT INTO webstore.catalog_product(masp,audience,kind) VALUES
              ('CUR-TM','UNISEX','kinh-mat'),('CUR-PM','UNISEX','ao-vest'),('CUR-HM','NU','dam-ngan');
            """,sql))await seed.ExecuteNonQueryAsync();
        async Task<ProductListResponse> Search(string query)=>(await client.GetFromJsonAsync<ProductListResponse>("/api/products?q=CUR-&pageSize=100&"+query))!;
        Assert.Equal(new[]{"CUR-HAT","CUR-HM","CUR-PM","CUR-SOCK","CUR-TM"},(await Search("audience=NAM")).Items.Select(p=>p.Id));
        Assert.Equal(new[]{"CUR-DF","CUR-HAT","CUR-SOCK","CUR-TF","CUR-UF"},(await Search("audience=NU")).Items.Select(p=>p.Id));
        Assert.Equal("CUR-PM",Assert.Single((await Search("audience=NAM&category=NEW-PM")).Items).Id);
        Assert.Equal("CUR-PM",Assert.Single((await Search("audience=NAM&kind=quan-dai")).Items).Id);
        Assert.Equal("CUR-HM",Assert.Single((await Search("audience=NAM&kind=ao-hoodie")).Items).Id);
        Assert.Empty((await Search("audience=NU&category=NEW-TM")).Items);
        Assert.Empty((await Search("audience=NU&kind=ao-hoodie")).Items);
        Assert.Equal(new[]{"CUR-HAT","CUR-SOCK"},(await Search("kind=phu-kien")).Items.Select(p=>p.Id));
        Assert.Equal("CUR-HAT",Assert.Single((await Search("kind=phu-kien&category=NEW-HAT")).Items).Id);
        Assert.Empty((await Search("kind=phu-kien&category=NEW-PM")).Items);
        var page=(await client.GetFromJsonAsync<ProductListResponse>("/api/products?q=CUR-&audience=NU&pageSize=2&page=2"))!;
        Assert.Equal(5,page.Total);Assert.Equal(new[]{"CUR-SOCK","CUR-TF"},page.Items.Select(p=>p.Id));
        var detail=(await client.GetFromJsonAsync<ProductDetailResponse>("/api/products/CUR-HM"))!;
        Assert.Equal("NAM",detail.Audience);Assert.Equal("ao-hoodie",detail.Kind);
        await using var original=new NpgsqlCommand("SELECT audience FROM webstore.catalog_product WHERE masp='CUR-HM'",sql);
        Assert.Equal("NU",await original.ExecuteScalarAsync()); // Resolution is read-only, preserving partner rows.
    }
}
