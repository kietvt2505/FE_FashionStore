using System.Net;
using System.Net.Http.Json;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Services;
using Npgsql;
using Xunit;
namespace FashionStore.Backend.Tests;
internal static class ShortOrderNumberScenarios
{
    internal static async Task VerifyAsync(HttpClient alice,HttpClient bob,NpgsqlConnection sql)
    {
        async Task Exec(string command){await using var cmd=new NpgsqlCommand(command,sql);await cmd.ExecuteNonQueryAsync();}
        async Task<long> Number(){await using var cmd=new NpgsqlCommand("SELECT last_value FROM webstore.web_order_number",sql);return (long)(await cmd.ExecuteScalarAsync())!;}
        // The earlier password-reset scenario revoked Bob's old session.
        var login=await bob.PostAsJsonAsync("/api/account/login",new LoginRequest{UserName="bob",Password="Synthetic reset password 123!"});
        Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        bob.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",(await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        var before=await Number();
        await Exec(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Short-Order-Numbers.sql")));
        Assert.Equal(before,await Number());
        // A manually entered invoice not present when the migration ran must not be reused.
        var reserved=WebOrderNumber.Format(before+1);
        await Exec($"INSERT INTO public.hoadon(sohd,ngaylap) VALUES('{reserved}',CURRENT_TIMESTAMP)");
        var request=new CheckoutRequest("Number Test","0900000001","Synthetic delivery address","COD",null,Guid.NewGuid());
        async Task<PlaceOrderResponse> Place(HttpClient client)
        {
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/cart/items",new CartItemRequest{Sku="SKU-L",Quantity=1})).StatusCode);
            var response=await client.PostAsJsonAsync("/api/orders",request);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
        }
        var first=await Place(alice);var second=await Place(bob);
        Assert.Equal(WebOrderNumber.Format(before+2),first.OrderId);
        Assert.NotEqual(first.OrderId,second.OrderId);Assert.NotEqual(reserved,second.OrderId);
        var retry=await alice.PostAsJsonAsync("/api/orders",request);
        Assert.Equal(first.OrderId,(await retry.Content.ReadFromJsonAsync<PlaceOrderResponse>())!.OrderId);
        Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync("/api/orders/"+first.OrderId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await alice.GetAsync("/api/orders/"+second.OrderId)).StatusCode);
        await using var count=new NpgsqlCommand("SELECT count(*) FROM webstore.order_snapshot WHERE request_id=@request",sql);
        count.Parameters.AddWithValue("request",request.RequestId);
        Assert.Equal(2L,(long)(await count.ExecuteScalarAsync())!);
    }
}
