using System.Net;
using System.Net.Http.Json;
using FashionStore.Backend.DTOs;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;
internal static class DailyOrderNumberScenarios
{
    internal static async Task VerifyAsync(HttpClient alice,HttpClient bob,NpgsqlConnection sql)
    {
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,sql);await cmd.ExecuteNonQueryAsync();}
        var migration=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Daily-Order-Numbers.sql"));
        await Exec(migration);await Exec(migration);
        async Task<string> Allocate()
        {
            await using var connection=new NpgsqlConnection(sql.ConnectionString);await connection.OpenAsync();
            await using var cmd=new NpgsqlCommand("SELECT webstore.allocate_order_number()",connection);
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        var codes=await Task.WhenAll(Enumerable.Range(0,24).Select(_=>Allocate()));
        Assert.Equal(codes.Length,codes.Distinct().Count());
        await using(var today=new NpgsqlCommand("SELECT 'HD'||to_char(CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh','DDMMYYYY')||'-'",sql)) {
            var prefix=(string)(await today.ExecuteScalarAsync())!;
            Assert.All(codes,code=>{Assert.StartsWith(prefix,code);Assert.Matches("^HD[0-9]{8}-[0-9]{4}$",code);Assert.DoesNotContain("-0000",code);});
        }
        await Exec("""
            INSERT INTO webstore.daily_order_number(sohd,order_day,suffix)
            SELECT 'HD01012099-'||lpad(n::text,4,'0'),DATE '2099-01-01',n FROM generate_series(3,9999) n;
            INSERT INTO public.hoadon(sohd,ngaylap) VALUES('HD01012099-0001',CURRENT_TIMESTAMP);
            """);
        await using(var last=new NpgsqlCommand("SELECT webstore.allocate_order_number(DATE '2099-01-01')",sql))
            Assert.Equal("HD01012099-0002",(string)(await last.ExecuteScalarAsync())!);
        await using(var exhausted=new NpgsqlCommand("SELECT webstore.allocate_order_number(DATE '2099-01-01')",sql))
            await Assert.ThrowsAsync<PostgresException>(async()=>await exhausted.ExecuteScalarAsync());
        await alice.DeleteAsync("/api/cart");
        await alice.PostAsJsonAsync("/api/cart/items",new CartItemRequest{Sku="SKU-L",Quantity=1});
        var request=new CheckoutRequest("Daily Number","0900000001","Synthetic address","COD",null,Guid.NewGuid());
        var response=await alice.PostAsJsonAsync("/api/orders",request);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var order=(await response.Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
        Assert.Matches("^HD[0-9]{8}-[0-9]{4}$",order.OrderId);
        Assert.Equal(order.OrderId,(await (await alice.PostAsJsonAsync("/api/orders",request)).Content.ReadFromJsonAsync<PlaceOrderResponse>())!.OrderId);
        Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync("/api/orders/"+order.OrderId)).StatusCode);
    }
}
