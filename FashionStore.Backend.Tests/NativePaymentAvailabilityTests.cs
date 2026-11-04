using FashionStore.Backend.Data;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Middleware;
using FashionStore.Backend.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class NativePaymentAvailabilityTests
{
    [Theory]
    [InlineData("PAYOS")]
    [InlineData("VISA")]
    [InlineData("VNPAY")]
    [InlineData("ZALOPAY")]
    public async Task UnsupportedOnlineMethodsCannotCreateAnOrderEvenWhenGatewayIsConfigured(string method)
    {
        var values=PayosTests.Configuration();values["Database:Provider"]="Supabase";
        values["Database:EnableSupabaseCustomerFeatures"]="true";values["Database:EnableSupabaseOrders"]="true";
        // Invalid on purpose: a regression that reaches a connection fails immediately.
        values["ConnectionStrings:Supabase"]="not-a-connection-string";
        var config=new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.True(new PaymentGatewaySettings(config).Ready("PAYOS"));
        var orders=new SupabaseOrderService(new DatabaseMode(config));
        var request=new CheckoutRequest("Synthetic customer","0900000099","Synthetic valid delivery address",method,null,Guid.NewGuid(),
            Items:new[]{new CartItemRequest{Sku="SYNTHETIC",Quantity=1}});
        var error=await Assert.ThrowsAsync<CustomerRequestException>(()=>orders.PlaceAsync("KH-SYNTHETIC",request,default));
        Assert.Equal(409,error.Status);Assert.Contains("chỉ hỗ trợ COD hoặc chuyển khoản",error.Message);
    }
}
