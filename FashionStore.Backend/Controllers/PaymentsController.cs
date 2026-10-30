using System.Security.Claims;
using FashionStore.Backend.Services;
using FashionStore.Backend.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Backend.Controllers;

[ApiController,Route("api/payments"),Authorize(Roles="Customer"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class PaymentsController(OnlinePaymentService payments,PaymentGatewayClient gateway,PaymentGatewaySettings settings):ControllerBase
{
    [HttpGet("options"),AllowAnonymous] public IReadOnlyList<OnlinePaymentOption> Options()=>payments.Options();
    public sealed record PaymentStartInput(string? ClientIp);
    [HttpPost("{id}/start")]
    public Task<PaymentLink> Start([System.ComponentModel.DataAnnotations.StringLength(30,MinimumLength=1)]string id,
        [FromBody(EmptyBodyBehavior=Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)]PaymentStartInput? input,CancellationToken ct)=>
        payments.Start(User.FindFirstValue(ClaimTypes.NameIdentifier)!,id,
            System.Net.IPAddress.TryParse(input?.ClientIp,out var address)?address.MapToIPv4().ToString():HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString()??"127.0.0.1",ct);
    [HttpPost("{id}/recheck")]
    public async Task<object> Recheck([System.ComponentModel.DataAnnotations.StringLength(30,MinimumLength=1)]string id,CancellationToken ct)=>new {
        status=await payments.Recheck(User.FindFirstValue(ClaimTypes.NameIdentifier)!,id,HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString()??"127.0.0.1",ct)
    };
    private Dictionary<string,string>? VnpQuery()=>Request.Query.Any(p=>p.Value.Count!=1)?null:
        Request.Query.ToDictionary(p=>p.Key,p=>p.Value.ToString(),StringComparer.Ordinal);
    private sealed record VnpReply(
        [property: System.Text.Json.Serialization.JsonPropertyName("RspCode")] string Code,
        [property: System.Text.Json.Serialization.JsonPropertyName("Message")] string Message);
    [AllowAnonymous,HttpGet("vnpay/ipn")]
    public async Task<IActionResult> VnpIpn(CancellationToken ct)
    {
        var query=VnpQuery();var payment=query==null?null:gateway.VerifyVnp(query);
        if(payment==null)return Ok(new VnpReply("97","Invalid signature"));
        try{await payments.Receive(payment,ct);return Ok(new VnpReply("00","Confirm Success"));}
        catch(CustomerRequestException e) when(e.Status==400){return Ok(new VnpReply("04","Invalid order or amount"));}
    }
    [AllowAnonymous,HttpGet("vnpay/return")]
    public async Task<IActionResult> VnpReturn(CancellationToken ct)
    {
        var query=VnpQuery();var payment=query==null?null:gateway.VerifyVnp(query);
        if(payment==null)return BadRequest(new {message="Kết quả thanh toán không hợp lệ. Vui lòng mở lịch sử đơn hàng để kiểm tra."});
        var order=await payments.ReturnOrder(payment,ct);
        // Browser return never changes payment, stock or loyalty. Only authenticated server notification settles it.
        return Redirect(settings.WebBase+(order==null?"/Orders":"/Orders/Details/"+Uri.EscapeDataString(order)));
    }
    public sealed record ZaloCallback(string Data,string Mac,int Type);
    [AllowAnonymous,HttpPost("zalopay/callback"),RequestSizeLimit(32768)]
    public async Task<IActionResult> ZaloNotify(ZaloCallback body,CancellationToken ct)
    {
        var payment=gateway.VerifyZalo(body.Data??"",body.Mac??"",body.Type);
        if(payment==null)return Ok(new {return_code=-1,return_message="Invalid signature"});
        try{await payments.Receive(payment,ct);return Ok(new {return_code=1,return_message="Success"});}
        catch(CustomerRequestException e) when(e.Status==400){return Ok(new {return_code=-1,return_message="Invalid order or amount"});}
    }
    [AllowAnonymous,HttpGet("zalopay/return")]
    public IActionResult ZaloReturn()=>settings.Ready("ZALOPAY")?Redirect(settings.WebBase+"/Orders"):StatusCode(503);
    [AllowAnonymous,HttpGet("payos/return")]
    public IActionResult PayosReturn()=>settings.Ready("PAYOS")?Redirect(settings.PayosWebBase+"/Orders"):StatusCode(503);
    [AllowAnonymous,HttpPost("payos/webhook"),RequestSizeLimit(32768)]
    public async Task<IActionResult> PayosNotify(System.Text.Json.JsonElement body,CancellationToken ct)
    {
        var payment=PayosGateway.Verify(body,settings);
        if(payment==null)return BadRequest(new{message="Invalid payment signature"});
        await payments.PayosNotify(payment,ct);
        return Ok(new{success=true});
    }
}
