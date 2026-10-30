using System.Net;
using System.Text.Json;
using FashionStore.Backend.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class PaymentGatewayTests
{
    internal static Dictionary<string,string?> Configuration()=>new() {
        ["Payments:PublicApiBaseUrl"]="https://api.example.test",["Payments:StorefrontBaseUrl"]="https://shop.example.test",
        ["Payments:VNPay:Enabled"]="true",["Payments:VNPay:EnableInternationalCards"]="true",
        ["Payments:VNPay:TmnCode"]="TESTONLY",["Payments:VNPay:HashSecret"]="synthetic-vnp-secret-only",
        ["Payments:ZaloPay:Enabled"]="true",["Payments:ZaloPay:AppId"]="1234",
        ["Payments:ZaloPay:Key1"]="synthetic-zalo-key1-only",["Payments:ZaloPay:Key2"]="synthetic-zalo-key2-only"
    };
    private static PaymentGatewayClient Client(Dictionary<string,string?>? values=null)=>new(
        new PaymentGatewaySettings(new ConfigurationBuilder().AddInMemoryCollection(values??Configuration()).Build()),new HttpClient(new GatewayStubHandler()));
    [Fact] public async Task CreatesSignedVnpUrlAndRejectsTamperedAmount()
    {
        var client=Client();var url=await client.Create("VISA","261028_abc",199000,"127.0.0.1",new DateTimeOffset(2026,10,28,3,0,0,TimeSpan.Zero),default);
        Assert.StartsWith("https://sandbox.vnpayment.vn/",url);Assert.Contains("vnp_BankCode=INTCARD",url);Assert.Contains("vnp_Amount=19900000",url);
        Assert.Contains("vnp_CreateDate=20261028100000",url);
        Assert.Contains("vnp_OrderType=200000",url);
        var query=new Uri(url).Query.TrimStart('?').Split('&').Select(x=>x.Split('=',2)).ToDictionary(x=>WebUtility.UrlDecode(x[0]),x=>WebUtility.UrlDecode(x[1]));
        query.Remove("vnp_SecureHash");query["vnp_ResponseCode"]="00";query["vnp_TransactionStatus"]="00";query["vnp_TransactionNo"]="12345";
        query["vnp_SecureHash"]=PaymentGatewayClient.Hmac("synthetic-vnp-secret-only",PaymentGatewayClient.VnpData(query),true);
        Assert.True(client.VerifyVnp(query)!.Paid);
        query["vnp_Amount"]="10000";Assert.Null(client.VerifyVnp(query));
    }
    [Fact] public void ZaloCallbackUsesKey2AndChecksMerchantAndEventType()
    {
        var client=Client();var data=JsonSerializer.Serialize(new {app_id=1234,app_trans_id="261028_abc",amount=199000,zp_trans_id=123456L});
        var mac=PaymentGatewayClient.Hmac("synthetic-zalo-key2-only",data);
        Assert.True(client.VerifyZalo(data,mac,1)!.Paid);
        Assert.Null(client.VerifyZalo(data,PaymentGatewayClient.Hmac("synthetic-zalo-key1-only",data),1));
        Assert.Null(client.VerifyZalo(data,mac,2));Assert.Null(client.VerifyZalo(data.Replace("199000","199001"),mac,1));
        var foreign=data.Replace("1234,","5555,");Assert.Null(client.VerifyZalo(foreign,PaymentGatewayClient.Hmac("synthetic-zalo-key2-only",foreign),1));
    }
    [Fact] public void MissingCredentialsOrPublicHttpsKeepsProvidersDisabled()
    {
        Assert.False(new PaymentGatewaySettings(new ConfigurationBuilder().Build()).Ready("VNPAY"));
        var values=Configuration();values["Payments:PublicApiBaseUrl"]="http://localhost:5000";
        Assert.False(new PaymentGatewaySettings(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Ready("VISA"));
        values=Configuration();values["Payments:VNPay:EnableInternationalCards"]="false";
        Assert.False(new PaymentGatewaySettings(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Ready("VISA"));
    }
    [Fact] public void QueryResponseRejectsWrongReferenceAndTamperedSignature()
    {
        var client=Client();var data=new Dictionary<string,string>{
            ["vnp_ResponseId"]="response1",["vnp_Command"]="querydr",["vnp_ResponseCode"]="00",["vnp_Message"]="Success",
            ["vnp_TmnCode"]="TESTONLY",["vnp_TxnRef"]="261028_query",["vnp_Amount"]="20000000",["vnp_BankCode"]="NCB",
            ["vnp_PayDate"]="20261028130000",["vnp_TransactionNo"]="123456",["vnp_TransactionType"]="01",["vnp_TransactionStatus"]="00",
            ["vnp_OrderInfo"]="Order",["vnp_PromotionCode"]="",["vnp_PromotionAmount"]=""
        };
        var fields=new[]{"vnp_ResponseId","vnp_Command","vnp_ResponseCode","vnp_Message","vnp_TmnCode","vnp_TxnRef","vnp_Amount","vnp_BankCode","vnp_PayDate","vnp_TransactionNo","vnp_TransactionType","vnp_TransactionStatus","vnp_OrderInfo","vnp_PromotionCode","vnp_PromotionAmount"};
        data["vnp_SecureHash"]=PaymentGatewayClient.Hmac("synthetic-vnp-secret-only",string.Join("|",fields.Select(k=>data[k])),true);
        Assert.Equal(200000,client.VerifyVnpQuery(data,"261028_query")!.Amount);
        Assert.Null(client.VerifyVnpQuery(data,"wrong-order"));
        data["vnp_Amount"]="10000";Assert.Null(client.VerifyVnpQuery(data,"261028_query"));
    }
}
internal sealed class GatewayStubHandler:HttpMessageHandler
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long,long> payosAmounts=new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        if(request.RequestUri!.Host=="api-merchant.payos.vn") {
            Assert.Equal("synthetic-client",request.Headers.GetValues("x-client-id").Single());
            Assert.Equal("synthetic-api",request.Headers.GetValues("x-api-key").Single());
            object data;
            if(request.Method==HttpMethod.Post) {
                using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));var root=body.RootElement;
                var order=root.GetProperty("orderCode").GetInt64();var amount=root.GetProperty("amount").GetInt64();payosAmounts[order]=amount;
                var text=$"amount={amount}&cancelUrl={root.GetProperty("cancelUrl").GetString()}&description={root.GetProperty("description").GetString()}&orderCode={order}&returnUrl={root.GetProperty("returnUrl").GetString()}";
                Assert.Equal(PaymentGatewayClient.Hmac("synthetic-payos-checksum-key-only-123",text),root.GetProperty("signature").GetString());
                data=new{orderCode=order,amount,bin="970422",accountNumber="00000000",accountName="SYNTHETIC",currency="VND",paymentLinkId=new string('a',32),checkoutUrl="https://pay.payos.vn/web/"+new string('a',32)};
            }else {
                var order=long.Parse(request.RequestUri.Segments.Last());var amount=payosAmounts.GetValueOrDefault(order,199000);
                data=new{id=new string('a',32),orderCode=order,amount,amountPaid=amount,amountRemaining=0,status="PAID",transactions=Array.Empty<object>()};
            }
            return new(HttpStatusCode.OK){Content=new StringContent(PayosTests.Envelope(data),System.Text.Encoding.UTF8,"application/json")};
        }
        return new(HttpStatusCode.OK){Content=new StringContent(request.RequestUri.AbsolutePath.EndsWith("/query")?"{\"return_code\":3}":"{\"return_code\":1,\"order_url\":\"https://qcgateway.zalopay.vn/checkout?order=synthetic\"}")};
    }
}
