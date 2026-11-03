using System.Data;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using FashionStore.Backend.Middleware;
using FashionStore.Backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace FashionStore.Backend.Services;

public sealed class SupabasePasswordRecoveryService(SupabaseCustomerFeaturesService data,IConfiguration config,
    IPasswordHasher<TaiKhoanKhachHang> hasher,ILogger<SupabasePasswordRecoveryService> logger,IRecoveryEmailSender? emailSender=null)
{
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public async Task Request(string email,CancellationToken ct)
    {
        var host=config["Recovery:SmtpHost"];var from=config["Recovery:From"];var resetUrl=config["Recovery:ResetUrl"];
        if(string.IsNullOrWhiteSpace(host)||string.IsNullOrWhiteSpace(from)||
            !Uri.TryCreate(resetUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo!=""||!string.IsNullOrEmpty(uri.Fragment))
            throw new CustomerRequestException(503,"Cửa hàng chưa cấu hình email và địa chỉ HTTPS khôi phục mật khẩu.");
        await using var c=data.Connection();await c.OpenAsync(ct);
        string customer,password;
        await using(var cmd=SupabaseCustomerFeaturesService.Command(c,null,"""
            SELECT makh,matkhau FROM public.khachhang WHERE lower(btrim(email))=lower(@email) AND trangthai
              AND NULLIF(btrim(tendangnhap),'') IS NOT NULL AND NULLIF(btrim(matkhau),'') IS NOT NULL LIMIT 2
            """,("email",email.Trim())))
        await using(var r=await cmd.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct))return;
            customer=r.GetString(0);password=r.GetString(1);
            if(await r.ReadAsync(ct))return;
        }
        var token=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await using(var save=SupabaseCustomerFeaturesService.Command(c,null,"""
            UPDATE public.khachhang SET makhoiphucbam=@hash,hethanmakhoiphuc=CURRENT_TIMESTAMP+INTERVAL '20 minutes',
                phienmatkhaukhoiphuc=@version
            WHERE makh=@kh AND matkhau=@password AND trangthai AND lower(btrim(email))=lower(@email)
            """,("hash",Hash(token)),("version",Hash(password+"\n"+email.Trim().ToLowerInvariant())),("kh",customer),("password",password),("email",email.Trim())))
            if(await save.ExecuteNonQueryAsync(ct)!=1)return;
        try
        {
            var link=resetUrl+(string.IsNullOrEmpty(uri.Query)?"?":"&")+"token="+Uri.EscapeDataString(token);
            await (emailSender??new SmtpRecoveryEmailSender(config)).SendAsync(from!,email.Trim(),link,ct);
        }
        catch(SmtpException){logger.LogWarning("Password recovery delivery failed; check SMTP configuration.");}
    }
    public async Task Reset(string token,string password,CancellationToken ct)
    {
        if(token.Length!=43)throw InvalidLink();
        await using var c=data.Connection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        string customer,current,version,currentEmail;
        await using(var cmd=SupabaseCustomerFeaturesService.Command(c,tx,"""
            SELECT makh,matkhau,phienmatkhaukhoiphuc,COALESCE(email,'') FROM public.khachhang
            WHERE makhoiphucbam=@hash AND hethanmakhoiphuc>CURRENT_TIMESTAMP AND trangthai
                AND NULLIF(btrim(tendangnhap),'') IS NOT NULL FOR UPDATE
            """,("hash",Hash(token))))
        await using(var r=await cmd.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct)||r.IsDBNull(1)||r.IsDBNull(2))throw InvalidLink();
            customer=r.GetString(0);current=r.GetString(1);version=r.GetString(2);currentEmail=r.GetString(3);
        }
        if(Hash(current+"\n"+currentEmail.Trim().ToLowerInvariant())!=version)throw InvalidLink();
        var next=hasher.HashPassword(new TaiKhoanKhachHang(),password);
        await using(var save=SupabaseCustomerFeaturesService.Command(c,tx,"""
            UPDATE public.khachhang SET matkhau=@password,makhoiphucbam=NULL,hethanmakhoiphuc=NULL,phienmatkhaukhoiphuc=NULL WHERE makh=@kh
            """,("password",next),("kh",customer)))await save.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }
    private static CustomerRequestException InvalidLink()=>new(400,"Liên kết không hợp lệ, đã dùng, hết hạn hoặc mật khẩu đã được thay đổi.");
}
