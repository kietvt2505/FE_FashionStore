using FashionStore.Backend.Middleware;
using Npgsql;
namespace FashionStore.Backend.Services;
internal static class SupabaseCheckoutPricing {
 internal sealed record Price(decimal Subtotal,decimal Discount,decimal Total,string? Voucher,string Rank,int Rate,int Points,bool MembershipInstalled,decimal MemberDiscount,decimal VoucherDiscount);
 internal static (string Rank,int Rate) Membership(decimal spent)=>spent>15000000m?("Kim Cương",10):spent>5000000m?("Vàng",5):("Bạc",0);
 internal static Task<bool> MembershipInstalled(NpgsqlConnection c,NpgsqlTransaction? t,CancellationToken ct)=>Task.FromResult(true);
 internal static Task<bool> IsInstalled(NpgsqlConnection c,NpgsqlTransaction? t,CancellationToken ct)=>Task.FromResult(true);
 internal static Task<bool> VouchersInstalled(NpgsqlConnection c,NpgsqlTransaction? t,CancellationToken ct)=>Task.FromResult(true);
 internal static async Task<Price> Calculate(NpgsqlConnection c,NpgsqlTransaction tx,bool installed,string customer,string? voucher,decimal subtotal,CancellationToken ct){
 decimal spent;int points;await using(var cmd=new NpgsqlCommand("SELECT tongchitieu,diemtichluy FROM public.khachhang WHERE makh=@kh AND trangthai FOR SHARE",c,tx)){cmd.Parameters.AddWithValue("kh",customer);await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CustomerRequestException(401,"Vui lòng đăng nhập lại.");spent=r.GetDecimal(0);points=r.GetInt32(1);}
 var member=Membership(spent);var md=Math.Round(subtotal*member.Rate/100m,0,MidpointRounding.AwayFromZero);decimal vd=0;string? code=null;
 if(!string.IsNullOrWhiteSpace(voucher)){
 await using var cmd=new NpgsqlCommand("SELECT k.makm,k.loaikm,k.giatrigiam,k.giatridonhangtoithieu,k.trangthai AND CURRENT_DATE BETWEEN k.ngaybatdau AND k.ngayketthuc,EXISTS(SELECT 1 FROM public.khuyenmaisanpham p WHERE p.makm=k.makm) FROM public.khuyenmai k WHERE upper(btrim(k.makm))=upper(btrim(@code)) FOR SHARE OF k",c,tx);cmd.Parameters.AddWithValue("code",voucher.Trim());await using var r=await cmd.ExecuteReaderAsync(ct);
 if(!await r.ReadAsync(ct)||!r.GetBoolean(4)||r.GetBoolean(5)||subtotal<(r.IsDBNull(3)?0:r.GetDecimal(3)))throw new CustomerRequestException(409,"Mã giảm giá không phù hợp, chưa bắt đầu hoặc đã hết hạn.");
 code=r.GetString(0);var kind=r.GetString(1).Trim().ToLowerInvariant();var value=r.GetDecimal(2);
 vd=kind is "phan_tram" or "phần trăm" or "phan tram" or "%"?Math.Round((subtotal-md)*value/100m,0,MidpointRounding.AwayFromZero):kind is "so_tien" or "số tiền" or "so tien" or "vnd"?value:0;
 if(value<=0||vd<=0||vd>=subtotal-md)throw new CustomerRequestException(409,"Mã giảm giá không hợp lệ.");
 if(await r.ReadAsync(ct))throw new CustomerRequestException(409,"Mã chưa xác định duy nhất.");
 }
 return new(subtotal,md+vd,subtotal-md-vd,code,member.Rank,member.Rate,points,true,md,vd);
 }
}