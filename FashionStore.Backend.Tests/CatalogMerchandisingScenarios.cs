using System.Net.Http.Json;
using FashionStore.Backend.DTOs;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class CatalogMerchandisingScenarios
{
    internal static async Task VerifyAsync(HttpClient client, NpgsqlConnection sql)
    {
        async Task Exec(string text) { await using var cmd = new NpgsqlCommand(text, sql); await cmd.ExecuteNonQueryAsync(); }
        var unavailable = (await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NAM"))!;
        Assert.Empty(unavailable.Items); Assert.NotNull(unavailable.Notice);
        await Exec("""
            INSERT INTO public.sanpham(masku,masp,maloai,tensanpham,kichco,mausac,dongia,soluongtonkho,trangthai) VALUES
            ('N1-S','N1','AO','Quần dài nam','S','Đen',500,5,true),
            ('N1-M','N1','AO','Quần dài nam','M','Đen',550,5,true),
            ('N2-S','N2','AO','Quần dài nam khác','S','Đen',200,5,true),
            ('F1-S','F1','AO','Quần dài nữ','S','Trắng',100,5,true),
            ('K1-S','K1','AO','Áo thun trẻ em','S','Trắng',50,5,true),
            ('U1-S','U1','AO','Quần dài chưa phân loại','S','Trắng',30,5,true),
            ('X1-S','X1','AO','Quần ngắn unisex','S','Trắng',70,5,true);
            """);
        var migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Supabase-Catalog-Merchandising.sql"));
        await Exec(migration);
        var unassigned=(await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NAM"))!;
        Assert.Empty(unassigned.Items); Assert.NotNull(unassigned.Notice);
        await Exec("""
            UPDATE webstore.catalog_product SET audience='NAM',regular_price=600 WHERE masp='N1';
            UPDATE webstore.catalog_product SET audience='NAM',regular_price=300 WHERE masp='N2';
            UPDATE webstore.catalog_product SET audience='NU',regular_price=120 WHERE masp='F1';
            UPDATE webstore.catalog_product SET audience='TRE_EM' WHERE masp='K1';
            UPDATE webstore.catalog_product SET audience='UNISEX' WHERE masp='X1';
            """);
        await Exec(migration); // Re-running must preserve assignments and sale annotations.
        var male = (await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NAM&kind=quan-dai&sort=price_asc&pageSize=1"))!;
        Assert.True(male.SupportsAudience); Assert.Equal(2, male.Total);
        Assert.Equal("N2", Assert.Single(male.Items).Id);
        Assert.Equal("NAM", male.Items[0].Audience); Assert.Equal(300, male.Items[0].OriginalPrice);
        var second = (await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NAM&kind=quan-dai&sort=price_asc&pageSize=1&page=2"))!;
        Assert.Equal("N1", Assert.Single(second.Items).Id); Assert.Equal(10, second.Items[0].Stock);
        var female = (await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NU&kind=quan-dai"))!;
        Assert.Equal("F1", Assert.Single(female.Items).Id);
        Assert.Equal("K1", Assert.Single((await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=TRE_EM"))!.Items).Id);
        Assert.Equal("X1", Assert.Single((await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NAM&kind=quan-ngan"))!.Items).Id);
        var sale = (await client.GetFromJsonAsync<ProductListResponse>("/api/products?saleOnly=true&sort=price_asc&audience=NAM"))!;
        Assert.Equal(new[] { "F1", "N2", "N1" }, sale.Items.Select(p => p.Id));
        Assert.Equal(3, sale.Total); Assert.All(sale.Items, p => Assert.True(p.OriginalPrice > p.Price));
        await Exec("UPDATE public.sanpham SET dongia=300 WHERE masp='N2'");
        sale = (await client.GetFromJsonAsync<ProductListResponse>("/api/products?saleOnly=true&sort=price_asc"))!;
        Assert.DoesNotContain(sale.Items, p => p.Id == "N2");
        var detail = (await client.GetFromJsonAsync<ProductDetailResponse>("/api/products/F1"))!;
        Assert.Equal(120, detail.OriginalPrice); Assert.Equal(100, detail.Price); Assert.Equal(100, detail.Variants[0].Price);
        Assert.Equal("quan-dai",detail.Kind);
        // The team assigns every product explicitly. Names never override those decisions.
        await Exec("""
            INSERT INTO public.sanpham(masku,masp,maloai,tensanpham,kichco,mausac,dongia,soluongtonkho,trangthai) VALUES
              ('MS-S','MS','AO','Quần ngắn mẫu nam','S','Đen',200,2,true),
              ('MS-M','MS','AO','Quần ngắn mẫu nam','M','Đỏ',300,3,true),
              ('FS-S','FS','AO','Quần ngắn mẫu nữ','S','Trắng',200,2,true),
              ('KS-S','KS','AO','Quần ngắn trẻ em','S','Đỏ',100,2,true),
              ('MG-S','MG','AO','Kính mát nam','S','Đen',100,2,true),
              ('UG-S','UG','AO','Kính mát dùng chung','S','Đen',100,2,true),
              ('FB-S','FB','AO','Túi xách nữ','S','Đen',100,2,true),
              ('KH-S','KH','AO','Mũ trẻ em','S','Đỏ',100,2,true),
              ('PENDING-S','PENDING','AO','Quần ngắn nam chưa được nhóm gán','S','Đen',100,2,true);
            INSERT INTO webstore.catalog_product(masp,audience,kind) VALUES
              ('MS','NAM','quan-ngan'),('FS','NU','quan-ngan'),('KS','TRE_EM','quan-ngan'),
              ('MG','NAM','kinh-mat'),('UG','UNISEX','kinh-mat'),('FB','NU','tui-xach'),('KH','TRE_EM','mu'),
              ('PENDING',NULL,'quan-ngan');
            """);
        var maleShorts=(await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NAM&kind=quan-ngan&pageSize=100"))!;
        Assert.Equal(new[]{"MS","X1"},maleShorts.Items.Select(p=>p.Id).OrderBy(id=>id));
        Assert.Equal(5,maleShorts.Items.Single(p=>p.Id=="MS").Stock);
        var femaleShorts=(await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NU&kind=quan-ngan&pageSize=100"))!;
        Assert.Equal(new[]{"FS","X1"},femaleShorts.Items.Select(p=>p.Id).OrderBy(id=>id));
        Assert.Equal("KS",Assert.Single((await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=TRE_EM&kind=quan-ngan"))!.Items).Id);
        Assert.Equal(new[]{"MG","UG"},(await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NAM&kind=phu-kien&pageSize=100"))!.Items.Select(p=>p.Id).OrderBy(id=>id));
        Assert.Equal(new[]{"FB","UG"},(await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NU&kind=phu-kien&pageSize=100"))!.Items.Select(p=>p.Id).OrderBy(id=>id));
        Assert.Equal("KH",Assert.Single((await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=TRE_EM&kind=phu-kien"))!.Items).Id);
        var filteredPage=(await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NAM&kind=quan-ngan&pageSize=1&page=2"))!;
        Assert.Equal(2,filteredPage.Total); Assert.Single(filteredPage.Items);
        // A matching size and color on different SKUs must still not satisfy the combined filter.
        Assert.Empty((await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NAM&kind=quan-ngan&size=M&color=Đen"))!.Items);
        Assert.Contains((await client.GetFromJsonAsync<ProductListResponse>("/api/products?pageSize=100"))!.Items,p=>p.Id=="PENDING");

        // Reviewed seed: exact current product IDs/names, separate from synthetic cases above.
        var names = new[]{"Áo thun","Áo sơ mi","Áo polo","Áo khoác gió","Áo hoodie","Áo vest",
            "Quần dài kaki","Quần dài jean","Quần ngắn kaki","Quần ngắn jeans","Quần jogger",
            "Mắt kính mát","Mắt kính râm","Nón","Dây nịch","Vớ","Váy ngắn","Váy dài",
            "Đầm ngắn","Đầm dài","Áo","Áo hoodie có lông","123"};
        for(var i=0;i<names.Length;i++) {
            await using var insert=new NpgsqlCommand("""
                INSERT INTO public.sanpham(masku,masp,maloai,tensanpham,kichco,mausac,dongia,soluongtonkho,trangthai)
                VALUES(@sku,@id,'AO',@name,'M','Đen',999,7,true)
                """,sql);
            insert.Parameters.AddWithValue("sku",$"SEED-{i+1:000}");
            insert.Parameters.AddWithValue("id",$"SP-{i+1:000}");
            insert.Parameters.AddWithValue("name",names[i]);
            await insert.ExecuteNonQueryAsync();
        }
        await Exec(migration);
        await Exec("UPDATE webstore.catalog_product SET regular_price=1200 WHERE masp='SP-001'");
        var assignments=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Catalog-Assignments.sql"));
        await Exec(assignments); await Exec(assignments);
        var seededMale=(await client.GetFromJsonAsync<ProductListResponse>("/api/products?q=SP-&audience=NAM&pageSize=100"))!;
        Assert.Equal(18,seededMale.Total);
        Assert.DoesNotContain(seededMale.Items,p=>p.Id=="SP-017" || p.Id=="SP-023");
        Assert.Equal(22,(await client.GetFromJsonAsync<ProductListResponse>("/api/products?q=SP-&audience=NU&pageSize=100"))!.Total);
        Assert.Equal(2,(await client.GetFromJsonAsync<ProductListResponse>("/api/products?q=SP-&audience=NAM&kind=quan-ngan"))!.Total);
        Assert.Equal(5,(await client.GetFromJsonAsync<ProductListResponse>("/api/products?q=SP-&kind=phu-kien"))!.Total);
        Assert.Equal("SP-015",Assert.Single((await client.GetFromJsonAsync<ProductListResponse>("/api/products?q=SP-&kind=day-nit"))!.Items).Id);
        Assert.Equal("SP-020",Assert.Single((await client.GetFromJsonAsync<ProductListResponse>("/api/products?q=SP-&audience=NU&kind=dam-dai"))!.Items).Id);
        Assert.Equal(22,(await client.GetFromJsonAsync<ProductListResponse>("/api/products?q=SP-&pageSize=100"))!.Total);
        Assert.Equal(System.Net.HttpStatusCode.NotFound,(await client.GetAsync("/api/products/SP-023")).StatusCode);
        await Exec("INSERT INTO public.loaisanpham VALUES ('TEST123',' 123 ',true)");
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories"))!,c=>c.Id=="TEST123");
        Assert.All(seededMale.Items,p=>{Assert.Equal(999,p.Price);Assert.Equal(7,p.Stock);});
        Assert.Equal(1200,seededMale.Items.Single(p=>p.Id=="SP-001").OriginalPrice);
        await Exec("UPDATE webstore.catalog_product SET audience='NAM' WHERE masp='SP-001'");
        await Exec(assignments);
        Assert.Equal("NAM",(await client.GetFromJsonAsync<ProductDetailResponse>("/api/products/SP-001"))!.Audience);
        await Exec("UPDATE public.sanpham SET tensanpham='Different product' WHERE masp='SP-002'");
        var changed=await Assert.ThrowsAsync<PostgresException>(()=>Exec(assignments));
        Assert.Equal("P0001",changed.SqlState);
        await Exec("ROLLBACK");
    }
}
