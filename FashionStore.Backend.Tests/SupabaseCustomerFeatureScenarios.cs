using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Services;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class SupabaseCustomerFeatureScenarios
{
    internal static async Task VerifyAsync(HttpClient alice, HttpClient bob, NpgsqlConnection sql)
    {
        async Task Exec(string text) { await using var cmd = new NpgsqlCommand(text, sql); await cmd.ExecuteNonQueryAsync(); }
        async Task<T> Scalar<T>(string text) { await using var cmd = new NpgsqlCommand(text, sql); return (T)(await cmd.ExecuteScalarAsync())!; }
        async Task<List<Dictionary<string, JsonElement>>> Rows(HttpClient client, string path) =>
            (await client.GetFromJsonAsync<List<Dictionary<string, JsonElement>>>("/api/customer/" + path))!;
        async Task Status(HttpStatusCode expected, Task<HttpResponseMessage> request)
        {
            using var response = await request;
            Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
        var migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Supabase-Customer-Features.sql"));
        await Exec("""
            CREATE TABLE public.khuyenmai(makm varchar(20) PRIMARY KEY,tenchuongtrinh text,loaikm text,giatrigiam numeric,
                giatridonhangtoithieu numeric,ngaybatdau date,ngayketthuc date,trangthai boolean);
            INSERT INTO public.khuyenmai VALUES
                ('ACTIVE','Ưu đãi hôm nay','Phần trăm',10,100000,CURRENT_DATE,CURRENT_DATE,true),
                ('OLD','Hết hạn','Phần trăm',5,0,CURRENT_DATE-10,CURRENT_DATE-1,true),
                ('FUTURE','Chưa bắt đầu','Phần trăm',5,0,CURRENT_DATE+1,CURRENT_DATE+10,true),
                ('HIDDEN','Đã tắt','Phần trăm',5,0,CURRENT_DATE-1,CURRENT_DATE+10,false);
            CREATE TABLE public.phieudoitra(sophieudt varchar(30) PRIMARY KEY,sohd varchar(30),trangthai varchar(20));
            CREATE TABLE public.chitietdoitra(sophieudt varchar(30),masku varchar(40),soluong integer);
            UPDATE public.hoadon SET ngaygiaothanhcong=LOCALTIMESTAMP-INTERVAL '1 day'
                WHERE trangthaigiaohang='Hàng giao thành công';
            """);
        await Exec(migration); await Exec(migration);
        var promotions = await Rows(alice, "promotions");
        Assert.Equal("ACTIVE", Assert.Single(promotions)["MaKM"].GetString());
        Assert.Equal("PHAN_TRAM", promotions[0]["LoaiKM"].GetString());
        Assert.False(promotions[0]["CheckoutSupported"].GetBoolean());
        var address = new { Recipient = "Alice Test", Phone = "0900000001", Address = "123 Synthetic Test Street" };
        await Status(HttpStatusCode.NoContent, alice.PostAsJsonAsync("/api/customer/addresses", address));
        var saved = Assert.Single(await Rows(alice, "addresses"));
        Assert.Equal(address.Recipient, saved["Recipient"].GetString());
        await Status(HttpStatusCode.NoContent, bob.DeleteAsync("/api/customer/addresses/" + saved["Id"].GetInt64()));
        Assert.Single(await Rows(alice, "addresses")); Assert.Empty(await Rows(bob, "addresses"));
        var edited = new { Recipient = "Alice Edited", Phone = "0900000011", Address = "456 Updated Synthetic Test Street" };
        await Status(HttpStatusCode.NotFound, bob.PutAsJsonAsync("/api/customer/addresses/" + saved["Id"].GetInt64(), edited));
        Assert.Equal(address.Recipient, Assert.Single(await Rows(alice, "addresses"))["Recipient"].GetString());
        // Quota is protected under concurrent inserts, not just a UI limit.
        for (var i = 1; i < 19; i++) await Status(HttpStatusCode.NoContent, alice.PostAsJsonAsync("/api/customer/addresses", address));
        var race = await Task.WhenAll(alice.PostAsJsonAsync("/api/customer/addresses", address), alice.PostAsJsonAsync("/api/customer/addresses", address));
        foreach (var response in race) Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict });
        if ((await Rows(alice, "addresses")).Count == 19) await Status(HttpStatusCode.NoContent, alice.PostAsJsonAsync("/api/customer/addresses", address));
        Assert.Equal(20, (await Rows(alice, "addresses")).Count);
        await Status(HttpStatusCode.NoContent, alice.PutAsJsonAsync("/api/customer/addresses/" + saved["Id"].GetInt64(), edited));
        var editedRows = await Rows(alice, "addresses");
        Assert.Equal(20, editedRows.Count);
        Assert.Equal(edited.Recipient, editedRows.Single(r=>r["Id"].GetInt64()==saved["Id"].GetInt64())["Recipient"].GetString());
        await Status(HttpStatusCode.BadRequest, alice.PutAsJsonAsync("/api/customer/addresses/" + saved["Id"].GetInt64(), new { Recipient="A", Phone="invalid", Address="short" }));
        await Status(HttpStatusCode.Conflict, alice.PostAsJsonAsync("/api/customer/addresses", address));
        await Status(HttpStatusCode.NoContent, alice.DeleteAsync("/api/customer/addresses/" + saved["Id"].GetInt64()));
        Assert.Equal(19, (await Rows(alice, "addresses")).Count);
        await Status(HttpStatusCode.NoContent, alice.PutAsync("/api/customer/wishlist/P1", null));
        await Status(HttpStatusCode.NoContent, alice.PutAsync("/api/customer/wishlist/P1", null));
        Assert.Equal("P1", Assert.Single(await Rows(alice, "wishlist"))["MaSP"].GetString());
        await Status(HttpStatusCode.NoContent, bob.DeleteAsync("/api/customer/wishlist/P1"));
        Assert.Single(await Rows(alice, "wishlist")); Assert.Empty(await Rows(bob, "wishlist"));
        await Status(HttpStatusCode.NotFound, alice.PutAsync("/api/customer/wishlist/NOT-FOUND", null));
        var points = Assert.Single(await Rows(alice, "points"));
        Assert.Equal(39, points["SoDiemThayDoi"].GetInt32()); Assert.Empty(await Rows(bob, "points"));
        var orderId = await Scalar<string>("SELECT sohd FROM public.hoadon WHERE trangthaigiaohang='Hàng giao thành công'");
        Assert.Equal(2, (await Rows(alice, "orders/" + orderId + "/history")).Count);
        Assert.Empty(await Rows(bob, "orders/" + orderId + "/history"));
        var details = (await alice.GetFromJsonAsync<OrderDetails>("/api/orders/" + orderId))!;
        Assert.True(details.SupportsHistory); Assert.True(details.SupportsReturns);
        var review = new ReviewInput { ProductId = "P1", OrderId = orderId, Stars = 5, Body = "Sản phẩm phù hợp, nội dung thử nghiệm." };
        var eligiblePath="reviews/P1/eligible-orders";
        Assert.Equal(orderId,Assert.Single(await Rows(alice,eligiblePath))["SoHD"].GetString());
        Assert.Empty(await Rows(bob,eligiblePath));
        Assert.Empty(await Rows(alice,"reviews/N1/eligible-orders"));
        var authorization=alice.DefaultRequestHeaders.Authorization;
        alice.DefaultRequestHeaders.Authorization=null;
        try { await Status(HttpStatusCode.Unauthorized,alice.GetAsync("/api/customer/"+eligiblePath)); }
        finally { alice.DefaultRequestHeaders.Authorization=authorization; }
        string? originalKind;
        await using(var kind=new NpgsqlCommand("SELECT loaihoadon FROM public.hoadon WHERE sohd=@hd",sql))
        { kind.Parameters.AddWithValue("hd",orderId); originalKind=await kind.ExecuteScalarAsync() as string; }
        async Task SetReviewState(string shipping,string payment,string? kind=null)
        {
            await using var cmd=new NpgsqlCommand("UPDATE public.hoadon SET trangthaigiaohang=@shipping,trangthaithanhtoan=@payment,loaihoadon=@kind WHERE sohd=@hd",sql);
            cmd.Parameters.AddWithValue("shipping",shipping);cmd.Parameters.AddWithValue("payment",payment);
            cmd.Parameters.AddWithValue("hd",orderId);
            cmd.Parameters.Add("kind",NpgsqlTypes.NpgsqlDbType.Varchar).Value=(object?)(kind??originalKind)??DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }
        // Prepaid orders still cannot be reviewed until the customer actually receives the goods.
        // The eligible list read above must not authorize a later POST after the status changes.
        foreach(var shipping in new[]{"Chờ xác nhận","Vừa xác nhận đơn","Chờ đơn vị vận chuyển tới lấy hàng","Shipper đã lấy hàng","Hàng đang được vận chuyển","UNKNOWN"})
        {
            await SetReviewState(shipping,"DA_THANH_TOAN");
            Assert.Empty(await Rows(alice,eligiblePath));
            await Status(HttpStatusCode.Conflict,alice.PostAsJsonAsync("/api/customer/reviews",review));
        }
        await SetReviewState("Hàng giao thành công","CHUA_THANH_TOAN");
        Assert.Empty(await Rows(alice,eligiblePath));
        await Status(HttpStatusCode.Conflict,alice.PostAsJsonAsync("/api/customer/reviews",review));
        await SetReviewState("Hàng giao thành công","DA_THANH_TOAN","WEB_HUY");
        Assert.Empty(await Rows(alice,eligiblePath));
        await Status(HttpStatusCode.Conflict,alice.PostAsJsonAsync("/api/customer/reviews",review));
        Assert.Equal(0L,await Scalar<long>("SELECT count(*) FROM webstore.product_review"));
        await SetReviewState("Hàng giao thành công","DA_THANH_TOAN");
        await Status(HttpStatusCode.Conflict,alice.PostAsJsonAsync("/api/customer/reviews",new ReviewInput
            {ProductId="N1",OrderId=orderId,Stars=5,Body="Not purchased"}));
        await Status(HttpStatusCode.Conflict,alice.PostAsJsonAsync("/api/customer/reviews",new ReviewInput
            {ProductId="P1",OrderId="NOT-MY-ORDER",Stars=5,Body="Not purchased"}));
        await Status(HttpStatusCode.Conflict, bob.PostAsJsonAsync("/api/customer/reviews", review));
        var reviewsRace=await Task.WhenAll(alice.PostAsJsonAsync("/api/customer/reviews",review),alice.PostAsJsonAsync("/api/customer/reviews",review));
        Assert.Single(reviewsRace,r=>r.StatusCode==HttpStatusCode.NoContent);
        Assert.Single(reviewsRace,r=>r.StatusCode==HttpStatusCode.Conflict);
        foreach(var response in reviewsRace)response.Dispose();
        Assert.Equal(1L,await Scalar<long>("SELECT count(*) FROM webstore.product_review"));
        Assert.Empty(await Rows(alice,eligiblePath));
        Assert.Empty(await Rows(alice, "reviews/P1")); // Pending reviews are never public.
        await Status(HttpStatusCode.Conflict, alice.PostAsJsonAsync("/api/customer/reviews", review));
        await Exec("UPDATE webstore.product_review SET status='HIEN_THI'");
        Assert.Equal(review.Body, Assert.Single(await Rows(bob, "reviews/P1"))["NoiDung"].GetString());
        await Status(HttpStatusCode.Conflict, alice.PostAsJsonAsync("/api/customer/reviews", new ReviewInput
            { ProductId = "N1", OrderId = orderId, Stars = 5, Body = "Not purchased" }));
        var request = new ReturnInput { RequestId = Guid.NewGuid(), OrderId = orderId, Sku = "SKU-L", Quantity = 1, Kind = "TRA", Reason = "Sản phẩm không phù hợp kích cỡ." };
        await Status(HttpStatusCode.Conflict, bob.PostAsJsonAsync("/api/customer/returns", request));
        await Exec("UPDATE public.hoadon SET ngaygiaothanhcong=LOCALTIMESTAMP-INTERVAL '16 days' WHERE trangthaigiaohang='Hàng giao thành công'");
        await Status(HttpStatusCode.Conflict, alice.PostAsJsonAsync("/api/customer/returns", request));
        await Exec("UPDATE public.hoadon SET ngaygiaothanhcong=LOCALTIMESTAMP-INTERVAL '1 day' WHERE trangthaigiaohang='Hàng giao thành công'");
        await Status(HttpStatusCode.NoContent, alice.PostAsJsonAsync("/api/customer/returns", request));
        await Status(HttpStatusCode.NoContent, alice.PostAsJsonAsync("/api/customer/returns", request)); // Exactly-once retry.
        request.Reason = "Changed request payload";
        await Status(HttpStatusCode.Conflict, alice.PostAsJsonAsync("/api/customer/returns", request));
        request.Reason = "Sản phẩm không phù hợp kích cỡ.";
        Assert.Equal("CHO_DUYET", Assert.Single(await Rows(alice, "returns"))["TrangThai"].GetString());
        Assert.Empty(await Rows(bob, "returns"));
        request.RequestId = Guid.NewGuid(); request.Quantity = 2;
        await Status(HttpStatusCode.Conflict, alice.PostAsJsonAsync("/api/customer/returns", request));
        request.Quantity = 1; request.Kind = "DOI"; request.NewSku = "INVALID";
        await Status(HttpStatusCode.Conflict, alice.PostAsJsonAsync("/api/customer/returns", request));
        request.Kind = "TRA";
        var returnsRace = await Task.WhenAll(alice.PostAsJsonAsync("/api/customer/returns", request), alice.PostAsJsonAsync("/api/customer/returns", new ReturnInput
            { RequestId = Guid.NewGuid(), OrderId = orderId, Sku = "SKU-L", Quantity = 1, Kind = "TRA", Reason = "Concurrent request" }));
        Assert.Single(returnsRace, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(returnsRace, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2L, await Scalar<long>("SELECT sum(quantity)::bigint FROM webstore.return_request"));
        Assert.Equal(8, await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-L'"));
        Assert.Equal(39, await Scalar<int>("SELECT diemtichluy FROM public.khachhang WHERE sodienthoai='0900000001'"));
        // A completed return from the shared WinForms tables also consumes the purchased quantity.
        await Exec($"UPDATE webstore.return_request SET status='TU_CHOI'; INSERT INTO public.phieudoitra VALUES('SHARED','{orderId}','HOAN_TAT'); INSERT INTO public.chitietdoitra VALUES('SHARED','SKU-L',2)");
        request.RequestId = Guid.NewGuid();
        await Status(HttpStatusCode.Conflict, alice.PostAsJsonAsync("/api/customer/returns", request));
        Assert.Equal(5L, await Scalar<long>("SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='webstore' AND c.relname IN ('address','wishlist','product_review','return_request','password_reset') AND c.relrowsecurity"));
        // Test reset without sending email: seed only a synthetic token hash, never return a raw token from the API.
        await Status(HttpStatusCode.ServiceUnavailable, bob.PostAsJsonAsync("/api/account/recovery", new { Email = "bob@example.test" }));
        const string token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var passwordHash = await Scalar<string>("SELECT matkhauhash FROM webstore.customer_account WHERE tendangnhap='bob'");
        await using (var seed = new NpgsqlCommand("INSERT INTO webstore.password_reset SELECT @hash,makh,@version,CURRENT_TIMESTAMP-INTERVAL '1 minute',NULL FROM webstore.customer_account WHERE tendangnhap='bob'", sql))
        {
            seed.Parameters.AddWithValue("hash", tokenHash); seed.Parameters.AddWithValue("version", CustomerSessions.Version(passwordHash)); await seed.ExecuteNonQueryAsync();
        }
        var reset = new { Token = token, Password = "Synthetic reset password 123!" };
        await Status(HttpStatusCode.BadRequest, bob.PostAsJsonAsync("/api/account/recovery/reset", reset));
        await Exec("UPDATE webstore.password_reset SET expiresatutc=CURRENT_TIMESTAMP+INTERVAL '20 minutes',passwordversion='stale'");
        await Status(HttpStatusCode.BadRequest, bob.PostAsJsonAsync("/api/account/recovery/reset", reset));
        await using (var fix = new NpgsqlCommand("UPDATE webstore.password_reset SET passwordversion=@version", sql))
        { fix.Parameters.AddWithValue("version", CustomerSessions.Version(passwordHash)); await fix.ExecuteNonQueryAsync(); }
        await Status(HttpStatusCode.NoContent, bob.PostAsJsonAsync("/api/account/recovery/reset", reset));
        await Status(HttpStatusCode.Unauthorized, bob.GetAsync("/api/account/me"));
        await Status(HttpStatusCode.BadRequest, bob.PostAsJsonAsync("/api/account/recovery/reset", reset));
        await Status(HttpStatusCode.OK, bob.PostAsJsonAsync("/api/account/login", new LoginRequest { UserName = "bob", Password = reset.Password }));
    }
}
