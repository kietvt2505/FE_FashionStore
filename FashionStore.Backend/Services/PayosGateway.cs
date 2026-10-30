using System.Globalization;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using FashionStore.Backend.Middleware;

namespace FashionStore.Backend.Services;

public sealed class PayosGateway(PaymentGatewaySettings settings,HttpClient http)
{
    private const string Endpoint="https://api-merchant.payos.vn/v2/payment-requests";
    private static readonly JsonSerializerOptions CanonicalJson=new(){Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    private static object? Sorted(JsonElement value)=>value.ValueKind switch {
        JsonValueKind.Object=>new SortedDictionary<string,object?>(value.EnumerateObject().ToDictionary(p=>p.Name,p=>Sorted(p.Value)),StringComparer.Ordinal),
        JsonValueKind.Array=>value.EnumerateArray().Select(Sorted).ToArray(),
        _=>value
    };
    internal static string SignatureData(JsonElement data)=>string.Join("&",data.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>{
        var v=p.Value;
        var text=v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined?"":v.ValueKind==JsonValueKind.String?v.GetString()??"":
            v.ValueKind is JsonValueKind.Array or JsonValueKind.Object?JsonSerializer.Serialize(Sorted(v),CanonicalJson):v.GetRawText();
        if(text is "null" or "undefined")text="";
        return p.Name+"="+text;
    }));
    internal static bool Signed(JsonElement envelope,string key)=>envelope.TryGetProperty("data",out var data) && data.ValueKind==JsonValueKind.Object &&
        envelope.TryGetProperty("signature",out var signature) && signature.ValueKind==JsonValueKind.String &&
        PaymentGatewayClient.SameHash(PaymentGatewayClient.Hmac(key,SignatureData(data)),signature.GetString());
    private async Task<JsonDocument> Send(HttpMethod method,string path,object? body,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(method,Endpoint+path);
        request.Headers.Add("x-client-id",settings.PayosClientId);request.Headers.Add("x-api-key",settings.PayosApiKey);
        if(body!=null)request.Content=JsonContent.Create(body);
        using var response=await http.SendAsync(request,ct);
        if(!response.IsSuccessStatusCode)throw new CustomerRequestException(503,"Chưa kết nối được payOS. Vui lòng kiểm tra lại đơn hàng sau.");
        var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if(document.RootElement.GetProperty("code").GetString()!="00" || !Signed(document.RootElement,settings.PayosChecksumKey)) {
            document.Dispose();throw new CustomerRequestException(503,"payOS chưa xác thực được giao dịch. Đơn hàng chưa được đánh dấu thanh toán.");
        }
        return document;
    }
    public async Task<string> Create(string attempt,long amount,DateTimeOffset created,CancellationToken ct)
    {
        var number=long.Parse(attempt,CultureInfo.InvariantCulture);
        var description="YD"+attempt[^Math.Min(7,attempt.Length)..];
        var returnUrl=settings.PayosApiBase+"/api/payments/payos/return";
        var sign=$"amount={amount.ToString(CultureInfo.InvariantCulture)}&cancelUrl={returnUrl}&description={description}&orderCode={attempt}&returnUrl={returnUrl}";
        using var response=await Send(HttpMethod.Post,"",new {orderCode=number,amount,description,cancelUrl=returnUrl,returnUrl,
            expiredAt=created.AddMinutes(15).ToUnixTimeSeconds(),signature=PaymentGatewayClient.Hmac(settings.PayosChecksumKey,sign)},ct);
        var data=response.RootElement.GetProperty("data");var link=data.GetProperty("paymentLinkId").GetString()!;
        if(data.GetProperty("orderCode").GetInt64()!=number || data.GetProperty("amount").GetInt64()!=amount ||
            data.GetProperty("bin").GetString()!=settings.PayosBin || data.GetProperty("accountNumber").GetString()!=settings.PayosAccount ||
            data.GetProperty("currency").GetString()!="VND" || !ValidLinkId(link) ||
            !Uri.TryCreate(data.GetProperty("checkoutUrl").GetString(),UriKind.Absolute,out var url) || url.Scheme!="https" || url.Host!="pay.payos.vn" || url.UserInfo!="" ||
            url.AbsolutePath!="/web/"+link || url.Query!="" || url.Fragment!="")
            throw new CustomerRequestException(503,"Thông tin tài khoản hoặc số tiền QR chưa khớp cấu hình cửa hàng. Vui lòng liên hệ cửa hàng.");
        return url.AbsoluteUri;
    }
    internal static bool ValidLinkId(string? id)=>id!=null && id.Length==32 && id.All(Uri.IsHexDigit);
    public async Task<GatewayEvent?> Query(string attempt,CancellationToken ct)
    {
        using var response=await Send(HttpMethod.Get,"/"+Uri.EscapeDataString(attempt),null,ct);
        var data=response.RootElement.GetProperty("data");var id=data.GetProperty("id").GetString();
        if(data.GetProperty("orderCode").GetInt64().ToString(CultureInfo.InvariantCulture)!=attempt || !ValidLinkId(id))
            throw new CustomerRequestException(503,"Kết quả QR không khớp giao dịch.");
        if(data.GetProperty("status").GetString()!="PAID")return null;
        var amount=data.GetProperty("amount").GetInt64();
        if(amount<=0 || data.GetProperty("amountPaid").GetInt64()<amount || data.GetProperty("amountRemaining").GetInt64()>0)return null;
        return new GatewayEvent("PAYOS",attempt,amount,"PAYOS:"+id,settings.PayosClientId,true){PaymentLinkId=id};
    }
    public static GatewayEvent? Verify(JsonElement envelope,PaymentGatewaySettings settings)
    {
        try {
            if(!settings.Ready("PAYOS") || !Signed(envelope,settings.PayosChecksumKey) || envelope.GetProperty("code").GetString()!="00" || !envelope.GetProperty("success").GetBoolean())return null;
            var data=envelope.GetProperty("data");var number=data.GetProperty("orderCode").GetInt64();var link=data.GetProperty("paymentLinkId").GetString();
            if(data.GetProperty("code").GetString()!="00" || data.GetProperty("currency").GetString()!="VND" || number<1 || !ValidLinkId(link))return null;
            return new GatewayEvent("PAYOS",number.ToString(CultureInfo.InvariantCulture),data.GetProperty("amount").GetDecimal(),"PAYOS:"+link,settings.PayosClientId,true){PaymentLinkId=link};
        }catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException){return null;}
    }
}
