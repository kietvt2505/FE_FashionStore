using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FashionStore.Backend.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class PayosTests
{
    internal static Dictionary<string,string?> Configuration()=>new(PaymentGatewayTests.Configuration()) {
        ["Payments:PayOS:Enabled"]="true",["Payments:PayOS:ClientId"]="synthetic-client",
        ["Payments:PayOS:ApiKey"]="synthetic-api",["Payments:PayOS:ChecksumKey"]="synthetic-payos-checksum-key-only-123",
        ["Payments:PayOS:BankBin"]="970422",["Payments:PayOS:AccountNumber"]="00000000"
    };
    internal static string Envelope(object data)
    {
        var json=JsonSerializer.SerializeToElement(data);
        return JsonSerializer.Serialize(new{code="00",success=true,data=json,signature=PaymentGatewayClient.Hmac("synthetic-payos-checksum-key-only-123",PayosGateway.SignatureData(json))});
    }
    [Fact] public void WebhookRejectsTamperingCurrencyAndWrongSignature()
    {
        var settings=new PaymentGatewaySettings(new ConfigurationBuilder().AddInMemoryCollection(Configuration()).Build());
        var data=new{orderCode=123456789L,paymentLinkId=new string('a',32),amount=199000,code="00",currency="VND"};
        using var good=JsonDocument.Parse(Envelope(data));
        Assert.NotNull(PayosGateway.Verify(good.RootElement,settings));
        using var changed=JsonDocument.Parse(Envelope(data).Replace("199000","199001"));
        Assert.Null(PayosGateway.Verify(changed.RootElement,settings));
        using var foreign=JsonDocument.Parse(Envelope(new{orderCode=123456789L,paymentLinkId=new string('a',32),amount=199000,code="00",currency="USD"}));
        Assert.Null(PayosGateway.Verify(foreign.RootElement,settings));
    }
    [Fact] public async Task CreatesQrWithTrustedRecipientAndVerifiesPaidQuery()
    {
        var settings=new PaymentGatewaySettings(new ConfigurationBuilder().AddInMemoryCollection(Configuration()).Build());
        var client=new PayosGateway(settings,new HttpClient(new GatewayStubHandler()));
        Assert.Equal("https://pay.payos.vn/web/"+new string('a',32),await client.Create("123456789",199000,DateTimeOffset.UtcNow,default));
        var paid=await client.Query("123456789",default);
        Assert.True(paid!.Paid);Assert.Equal(199000,paid.Amount);
        var wrong=Configuration();wrong["Payments:PayOS:AccountNumber"]="another-account";
        await Assert.ThrowsAsync<FashionStore.Backend.Middleware.CustomerRequestException>(()=>new PayosGateway(
            new PaymentGatewaySettings(new ConfigurationBuilder().AddInMemoryCollection(wrong).Build()),new HttpClient(new GatewayStubHandler())).Create("123456789",199000,DateTimeOffset.UtcNow,default));
    }
}
