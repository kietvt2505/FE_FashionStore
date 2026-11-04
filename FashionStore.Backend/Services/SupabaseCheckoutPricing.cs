using System.Globalization;
using System.Text;
using FashionStore.Backend.Middleware;
using Npgsql;

namespace FashionStore.Backend.Services;

internal static class SupabaseCheckoutPricing
{
    internal sealed record Price(decimal Subtotal,decimal Discount,decimal Total,string? Voucher,string Rank,int Rate,int Points,bool MembershipInstalled,decimal MemberDiscount,decimal VoucherDiscount,decimal VatAmount,bool VoucherIgnored);
    internal static (string Rank,int Rate) Membership(decimal spent)=>spent>15000000m?("Kim Cương",10):spent>5000000m?("Vàng",5):("Bạc",0);
    internal static Task<bool> MembershipInstalled(NpgsqlConnection c,NpgsqlTransaction? t,CancellationToken ct)=>Task.FromResult(true);
    internal static Task<bool> IsInstalled(NpgsqlConnection c,NpgsqlTransaction? t,CancellationToken ct)=>Task.FromResult(true);
    internal static Task<bool> VouchersInstalled(NpgsqlConnection c,NpgsqlTransaction? t,CancellationToken ct)=>Task.FromResult(true);
    private static decimal Round(decimal value)=>Math.Round(value,0,MidpointRounding.AwayFromZero);
    private static string Kind(string value)=>new string(value.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray()).Replace('đ','d').Replace('Đ','D').Replace('_',' ').Trim().ToUpperInvariant();
    internal static async Task<Price> Calculate(NpgsqlConnection c,NpgsqlTransaction tx,bool installed,string customer,string? voucher,decimal subtotal,CancellationToken ct)
    {
        decimal spent;int points;
        // POS may hold this customer while waiting for products; avoid a reverse lock cycle.
        await using(var cmd=new NpgsqlCommand("SELECT tongchitieu,diemtichluy FROM public.khachhang WHERE makh=@kh AND trangthai",c,tx))
        {
            cmd.Parameters.AddWithValue("kh",customer);await using var r=await cmd.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new CustomerRequestException(401,"Vui lòng đăng nhập lại.");
            spent=r.GetDecimal(0);points=r.GetInt32(1);
        }
        var member=Membership(spent);var memberAmount=Round(subtotal*member.Rate/100m);decimal voucherAmount=0;string? requested=null;
        if(!string.IsNullOrWhiteSpace(voucher))
        {
            await using var cmd=new NpgsqlCommand("""
                SELECT k.makm,k.loaikm,k.giatrigiam FROM public.khuyenmai k
                WHERE upper(btrim(k.makm))=upper(btrim(@code)) AND k.trangthai
                  AND (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh') BETWEEN k.ngaybatdau AND k.ngayketthuc
                  AND COALESCE(k.giatridonhangtoithieu,0)<=@subtotal
                  AND NOT EXISTS(SELECT 1 FROM public.khuyenmaisanpham p WHERE p.makm=k.makm)
                FOR SHARE OF k
                """,c,tx);
            cmd.Parameters.AddWithValue("code",voucher.Trim());cmd.Parameters.AddWithValue("subtotal",subtotal);
            await using var r=await cmd.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new CustomerRequestException(409,"Mã giảm giá không phù hợp, chưa bắt đầu hoặc đã hết hạn.");
            requested=r.GetString(0);var value=r.GetDecimal(2);
            if(value<=0)throw new CustomerRequestException(409,"Mã giảm giá không hợp lệ.");
            voucherAmount=Kind(r.GetString(1)) is "SO TIEN" or "VND"?Round(value):Round(subtotal*value/100m);
            if(await r.ReadAsync(ct))throw new CustomerRequestException(409,"Mã chưa xác định duy nhất.");
        }
        // POS: greatest monetary saving, member wins ties, VAT after discount.
        var voucherWins=voucherAmount>memberAmount;
        var discount=Math.Min(subtotal,Math.Max(memberAmount,voucherAmount));
        var vat=Round((subtotal-discount)*0.08m);
        return new(subtotal,discount,subtotal-discount+vat,voucherWins?requested:null,member.Rank,member.Rate,points,true,
            voucherWins?0:discount,voucherWins?discount:0,vat,requested!=null&&!voucherWins);
    }
}
