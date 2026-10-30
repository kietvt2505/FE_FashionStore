using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FashionStore.Backend.Middleware;

namespace FashionStore.Backend.Services;

public sealed class PaymentGatewaySettings(IConfiguration config)
{
    public bool Sandbox => config.GetValue("Payments:Sandbox", true);
    public string ApiBase => (config["Payments:PublicApiBaseUrl"] ?? "").TrimEnd('/');
    public string WebBase => (config["Payments:StorefrontBaseUrl"] ?? "").TrimEnd('/');
    public string TmnCode => config["Payments:VNPay:TmnCode"] ?? "";
    public string VnpSecret => config["Payments:VNPay:HashSecret"] ?? "";
    public string AppId => config["Payments:ZaloPay:AppId"] ?? "";
    public string Key1 => config["Payments:ZaloPay:Key1"] ?? "";
    public string Key2 => config["Payments:ZaloPay:Key2"] ?? "";
    public string PayosClientId => config["Payments:PayOS:ClientId"] ?? "";
    public string PayosApiKey => config["Payments:PayOS:ApiKey"] ?? "";
    public string PayosChecksumKey => config["Payments:PayOS:ChecksumKey"] ?? "";
    public string PayosAccount => config["Payments:PayOS:AccountNumber"] ?? "";
    public string PayosBin => config["Payments:PayOS:BankBin"] ?? "";
    public string PayosApiBase => config["Payments:PayOS:LocalDevelopment"]=="true" && ApiBase==""?"http://127.0.0.1:5000":ApiBase;
    public string PayosWebBase => config["Payments:PayOS:LocalDevelopment"]=="true" && WebBase==""?"http://localhost:58472":WebBase;
    private bool PayosUrl(string url)=>Https(url) || (config["Payments:PayOS:LocalDevelopment"]=="true" &&
        Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.IsLoopback && uri.Scheme=="http" && uri.UserInfo=="" && uri.Query=="" && uri.Fragment=="");
    private static bool Https(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && !uri.IsLoopback && uri.UserInfo == "" && uri.Query == "" && uri.Fragment == "";
    public bool Ready(string method) => method=="PAYOS" ? config.GetValue<bool>("Payments:PayOS:Enabled") &&
        PayosClientId.Length>0 && PayosApiKey.Length>0 && PayosChecksumKey.Length>=32 && PayosAccount.Length>0 && PayosBin.Length==6 &&
        PayosUrl(PayosApiBase) && PayosUrl(PayosWebBase) : Https(ApiBase) && Https(WebBase) && (method switch
    {
        "VNPAY" => config.GetValue<bool>("Payments:VNPay:Enabled") && TmnCode.Length > 0 && VnpSecret.Length >= 16,
        "VISA" => config.GetValue<bool>("Payments:VNPay:Enabled") && config.GetValue<bool>("Payments:VNPay:EnableInternationalCards") && TmnCode.Length > 0 && VnpSecret.Length >= 16,
        "ZALOPAY" => config.GetValue<bool>("Payments:ZaloPay:Enabled") && int.TryParse(AppId, out var id) && id > 0 && Key1.Length >= 16 && Key2.Length >= 16,
        _ => false
    });
    public static bool Online(string method) => method is "VNPAY" or "VISA" or "ZALOPAY" or "PAYOS";
    public string Merchant(string method) => method == "PAYOS"?PayosClientId:method == "ZALOPAY" ? AppId : TmnCode;
    public string VnpEndpoint => Sandbox ? "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html" : "https://pay.vnpay.vn/vpcpay.html";
    public string ZaloEndpoint => Sandbox ? "https://sb-openapi.zalopay.vn/v2/create" : "https://openapi.zalopay.vn/v2/create";
    public string VnpQueryEndpoint => Sandbox ? "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction" : "https://pay.vnpay.vn/merchant_webapi/api/transaction";
    public string ZaloQueryEndpoint => Sandbox ? "https://sb-openapi.zalopay.vn/v2/query" : "https://openapi.zalopay.vn/v2/query";
    public static long Amount(decimal amount)
    {
        if (amount <= 0 || amount > 999999999 || amount != decimal.Truncate(amount))
            throw new CustomerRequestException(409, "Thanh toán trực tuyến cần số tiền nguyên VND từ 1 đến 999.999.999đ.");
        return (long)amount;
    }
}

public sealed record GatewayEvent(string Provider, string AttemptId, decimal Amount, string TransactionId, string Merchant, bool Paid)
{
    public string? PaymentLinkId {get;init;}
}
public sealed record PaymentLink(string Url, string OrderId);
public sealed record OnlinePaymentOption(string Code, string Label, bool Enabled);

public sealed class PaymentGatewayClient(PaymentGatewaySettings settings, HttpClient http)
{
    internal static string Hmac(string secret, string data, bool sha512 = false) => Convert.ToHexString(sha512
        ? HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data))
        : HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    internal static bool SameHash(string expected, string? actual)
    {
        if (actual == null || expected.Length != actual.Length) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual)); }
        catch (FormatException) { return false; }
    }
    internal static string VnpData(IEnumerable<KeyValuePair<string, string>> data) => string.Join("&", data
        .Where(p => p.Key.StartsWith("vnp_", StringComparison.Ordinal) && p.Key is not "vnp_SecureHash" and not "vnp_SecureHashType" && p.Value.Length > 0)
        .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => WebUtility.UrlEncode(p.Key) + "=" + WebUtility.UrlEncode(p.Value)));
    public async Task<string> Create(string method, string attempt, decimal amount, string ip, DateTimeOffset created, CancellationToken ct)
    {
        if (!settings.Ready(method)) throw new CustomerRequestException(503, "Phương thức thanh toán chưa được cấu hình tại cửa hàng.");
        var money = PaymentGatewaySettings.Amount(amount);
        if(method=="PAYOS")return await new PayosGateway(settings,http).Create(attempt,money,created,ct);
        if (method != "ZALOPAY")
        {
            var time = created.ToOffset(TimeSpan.FromHours(7));
            var data = new Dictionary<string, string> {
                ["vnp_Version"]="2.1.0", ["vnp_Command"]="pay", ["vnp_TmnCode"]=settings.TmnCode,
                ["vnp_Amount"]=(money*100L).ToString(CultureInfo.InvariantCulture), ["vnp_CurrCode"]="VND",
                ["vnp_TxnRef"]=attempt, ["vnp_OrderInfo"]="Thanh toan don hang YODY", ["vnp_OrderType"]="200000",
                ["vnp_Locale"]="vn", ["vnp_IpAddr"]=ip, ["vnp_ReturnUrl"]=settings.ApiBase+"/api/payments/vnpay/return",
                ["vnp_CreateDate"]=time.ToString("yyyyMMddHHmmss"), ["vnp_ExpireDate"]=time.AddMinutes(15).ToString("yyyyMMddHHmmss"),
                ["vnp_BankCode"]=method=="VISA"?"INTCARD":"VNBANK"
            };
            var query=VnpData(data);
            return settings.VnpEndpoint+"?"+query+"&vnp_SecureHash="+Hmac(settings.VnpSecret,query,true);
        }
        var embed=JsonSerializer.Serialize(new { redirecturl=settings.ApiBase+"/api/payments/zalopay/return" });
        var payload=new Dictionary<string,string> {
            ["app_id"]=settings.AppId, ["app_trans_id"]=attempt, ["app_user"]="yody-web", ["amount"]=money.ToString(CultureInfo.InvariantCulture),
            ["app_time"]=created.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), ["item"]="[]", ["embed_data"]=embed,
            ["description"]="Thanh toán đơn hàng YODY", ["bank_code"]="zalopayapp", ["expire_duration_seconds"]="900",
            ["callback_url"]=settings.ApiBase+"/api/payments/zalopay/callback"
        };
        payload["mac"]=Hmac(settings.Key1,string.Join("|",settings.AppId,attempt,payload["app_user"],payload["amount"],payload["app_time"],embed,"[]"));
        using var response=await http.PostAsync(settings.ZaloEndpoint,new FormUrlEncodedContent(payload),ct);
        if(!response.IsSuccessStatusCode)throw new CustomerRequestException(503,"Cổng thanh toán chưa phản hồi. Đơn hàng vẫn được giữ để kiểm tra.");
        using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root=document.RootElement;
        if(root.GetProperty("return_code").GetInt32()!=1 || !root.TryGetProperty("order_url",out var link) ||
           !Uri.TryCreate(link.GetString(),UriKind.Absolute,out var uri) || uri.Scheme!="https" ||
           !uri.Host.EndsWith(".zalopay.vn",StringComparison.OrdinalIgnoreCase) || uri.UserInfo!="")
            throw new CustomerRequestException(503,"Chưa thể mở thanh toán ZaloPay. Vui lòng kiểm tra trạng thái đơn trước khi thử lại.");
        return uri.AbsoluteUri;
    }
    public GatewayEvent? VerifyVnp(IReadOnlyDictionary<string,string> data)
    {
        string Get(string key)=>data.GetValueOrDefault(key)??"";
        if(!settings.Ready("VNPAY") || Get("vnp_TmnCode")!=settings.TmnCode ||
           !SameHash(Hmac(settings.VnpSecret,VnpData(data),true),Get("vnp_SecureHash")) ||
           !long.TryParse(Get("vnp_Amount"),NumberStyles.None,CultureInfo.InvariantCulture,out var amount) || amount<=0 || amount%100!=0 ||
           !ValidId(Get("vnp_TxnRef"))) return null;
        var paid=Get("vnp_ResponseCode")=="00" && Get("vnp_TransactionStatus")=="00";
        if(paid && (!long.TryParse(Get("vnp_TransactionNo"),out var transaction) || transaction<=0))return null;
        return new("VNPAY",Get("vnp_TxnRef"),amount/100m,Get("vnp_TransactionNo"),settings.TmnCode,paid);
    }
    public GatewayEvent? VerifyZalo(string data,string mac,int type)
    {
        if(type!=1 || !settings.Ready("ZALOPAY") || !SameHash(Hmac(settings.Key2,data),mac))return null;
        try {
            using var doc=JsonDocument.Parse(data);var value=doc.RootElement;
            var app=value.GetProperty("app_id").GetInt32().ToString(CultureInfo.InvariantCulture);
            var attempt=value.GetProperty("app_trans_id").GetString()??"";
            var amount=value.GetProperty("amount").GetDecimal();var transaction=value.GetProperty("zp_trans_id").GetInt64();
            if(app!=settings.AppId || !ValidId(attempt) || amount<=0 || transaction<=0 || amount!=decimal.Truncate(amount))return null;
            return new("ZALOPAY",attempt,amount,transaction.ToString(CultureInfo.InvariantCulture),app,true);
        }catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){return null;}
    }
    public async Task<GatewayEvent?> Query(string method,string attempt,DateTimeOffset created,DateTimeOffset now,string ip,CancellationToken ct)
    {
        if(!settings.Ready(method))throw new CustomerRequestException(503,"Phương thức thanh toán chưa được cấu hình tại cửa hàng.");
        if(method=="PAYOS")return await new PayosGateway(settings,http).Query(attempt,ct);
        if(method=="ZALOPAY") {
            var form=new Dictionary<string,string>{["app_id"]=settings.AppId,["app_trans_id"]=attempt,
                ["mac"]=Hmac(settings.Key1,settings.AppId+"|"+attempt+"|"+settings.Key1)};
            using var response=await http.PostAsync(settings.ZaloQueryEndpoint,new FormUrlEncodedContent(form),ct);
            response.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));var root=doc.RootElement;
            if(root.GetProperty("return_code").GetInt32()!=1)return null;
            var transaction=root.GetProperty("zp_trans_id").GetInt64();var amount=root.GetProperty("amount").GetDecimal();
            if(transaction<=0 || amount<=0)return null;
            return new("ZALOPAY",attempt,amount,transaction.ToString(CultureInfo.InvariantCulture),settings.AppId,true);
        }
        var values=new Dictionary<string,string>{["vnp_RequestId"]=Guid.NewGuid().ToString("N"),["vnp_Version"]="2.1.0",["vnp_Command"]="querydr",
            ["vnp_TmnCode"]=settings.TmnCode,["vnp_TxnRef"]=attempt,["vnp_TransactionDate"]=created.ToOffset(TimeSpan.FromHours(7)).ToString("yyyyMMddHHmmss"),
            ["vnp_CreateDate"]=now.ToOffset(TimeSpan.FromHours(7)).ToString("yyyyMMddHHmmss"),["vnp_IpAddr"]=ip,["vnp_OrderInfo"]="Kiem tra thanh toan YODY"};
        values["vnp_SecureHash"]=Hmac(settings.VnpSecret,string.Join("|",new[]{"vnp_RequestId","vnp_Version","vnp_Command","vnp_TmnCode","vnp_TxnRef","vnp_TransactionDate","vnp_CreateDate","vnp_IpAddr","vnp_OrderInfo"}.Select(k=>values[k])),true);
        using var result=await http.PostAsJsonAsync(settings.VnpQueryEndpoint,values,ct);result.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await result.Content.ReadAsStringAsync(ct));
        var data=json.RootElement.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.ToString());
        return VerifyVnpQuery(data,attempt);
    }
    internal GatewayEvent? VerifyVnpQuery(IReadOnlyDictionary<string,string> values,string attempt)
    {
        string Get(string key)=>values.GetValueOrDefault(key)??"";
        var fields=new[]{"vnp_ResponseId","vnp_Command","vnp_ResponseCode","vnp_Message","vnp_TmnCode","vnp_TxnRef","vnp_Amount","vnp_BankCode","vnp_PayDate","vnp_TransactionNo","vnp_TransactionType","vnp_TransactionStatus","vnp_OrderInfo","vnp_PromotionCode","vnp_PromotionAmount"};
        if(Get("vnp_Command")!="querydr" || Get("vnp_ResponseCode")!="00" || Get("vnp_TmnCode")!=settings.TmnCode || Get("vnp_TxnRef")!=attempt ||
           !SameHash(Hmac(settings.VnpSecret,string.Join("|",fields.Select(Get)),true),Get("vnp_SecureHash")) ||
           Get("vnp_TransactionType")!="01" || Get("vnp_TransactionStatus")!="00" ||
           !long.TryParse(Get("vnp_Amount"),NumberStyles.None,CultureInfo.InvariantCulture,out var money) || money<=0 || money%100!=0 ||
           !long.TryParse(Get("vnp_TransactionNo"),out var transaction) || transaction<=0)return null;
        return new("VNPAY",attempt,money/100m,Get("vnp_TransactionNo"),settings.TmnCode,true);
    }
    internal static bool ValidId(string value)=>value.Length is >0 and <=40 && value.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_');
}
