using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FashionStore.Backend.DTOs;

namespace FashionStore.Backend.Services;

internal static class NativeOrderRequestFingerprint
{
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    // Existing varchar(64): 128-bit request identity + 128-bit payload digest. This
    // remembers a requested but unapplied voucher without adding columns or notes.
    public static (string Legacy,string Prefix,string Value) Create(string customer,CheckoutRequest request)
    {
        var legacy=Hash(customer+":"+request.RequestId.ToString("D"));
        var payload=JsonSerializer.Serialize(new{Recipient=request.Recipient.Trim(),Phone=request.Phone.Trim(),Address=request.Address.Trim(),request.PaymentMethod,
            Note=request.Note?.Trim()??"",Voucher=request.Voucher?.Trim().ToUpperInvariant()??"",
            Items=request.Items!.GroupBy(i=>i.Sku,StringComparer.Ordinal).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new{Sku=g.Key,Quantity=g.Sum(i=>(long)i.Quantity)})});
        return (legacy,legacy[..32],legacy[..32]+Hash(payload)[..32]);
    }
}
