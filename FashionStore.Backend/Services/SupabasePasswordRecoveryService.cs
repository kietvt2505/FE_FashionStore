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

public sealed class SupabasePasswordRecoveryService(SupabaseCustomerFeaturesService data, IConfiguration config,
    IPasswordHasher<TaiKhoanKhachHang> hasher, ILogger<SupabasePasswordRecoveryService> logger)
{
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task Request(string email, CancellationToken ct)
    {
        var host = config["Recovery:SmtpHost"]; var from = config["Recovery:From"]; var resetUrl = config["Recovery:ResetUrl"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) ||
            !Uri.TryCreate(resetUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new CustomerRequestException(503, "Khôi phục mật khẩu chưa được bật. Cửa hàng cần cấu hình email và địa chỉ HTTPS nhận liên kết.");
        await using var c = data.Connection(); await c.OpenAsync(ct);
        string customer; string version;
        await using (var cmd = SupabaseCustomerFeaturesService.Command(c, null, """
            SELECT a.makh,a.matkhauhash FROM webstore.customer_account a JOIN public.khachhang k USING(makh)
            WHERE lower(btrim(k.email))=lower(@email) AND a.trangthai AND k.trangthai
            """, ("email", email.Trim())))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return;
            customer = r.GetString(0); version = CustomerSessions.Version(r.GetString(1));
            if (await r.ReadAsync(ct)) return;
        }
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await using (var cmd = SupabaseCustomerFeaturesService.Command(c, null, """
            INSERT INTO webstore.password_reset(tokenhash,makh,passwordversion,expiresatutc)
            VALUES(@hash,@kh,@version,CURRENT_TIMESTAMP+INTERVAL '20 minutes')
            """, ("hash", Hash(token)), ("kh", customer), ("version", version)))
            await cmd.ExecuteNonQueryAsync(ct);
        // Keep the same response for unknown addresses and never expose the token in HTTP or logs.
        try
        {
            using var smtp = new SmtpClient(host, config.GetValue<int?>("Recovery:SmtpPort") ?? 587) { EnableSsl = true };
            if (!string.IsNullOrWhiteSpace(config["Recovery:SmtpUser"]))
                smtp.Credentials = new NetworkCredential(config["Recovery:SmtpUser"], config["Recovery:SmtpPassword"]);
            using var message = new MailMessage(from!, email.Trim(), "YODY — khôi phục mật khẩu",
                "Liên kết có hiệu lực 20 phút, dùng một lần:\n" + resetUrl + "?token=" + Uri.EscapeDataString(token) +
                "\nNếu bạn không yêu cầu, hãy bỏ qua email này.");
            await smtp.SendMailAsync(message, ct);
        }
        catch (SmtpException) { logger.LogWarning("Password recovery delivery failed; check SMTP configuration."); }
    }

    public async Task Reset(string token, string password, CancellationToken ct)
    {
        if (token.Length != 43) throw new CustomerRequestException(400, "Liên kết không hợp lệ hoặc đã hết hạn.");
        await using var c = data.Connection(); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        string customer; string version; string current;
        await using (var cmd = SupabaseCustomerFeaturesService.Command(c, tx, """
            SELECT a.makh,r.passwordversion,a.matkhauhash FROM webstore.password_reset r
            JOIN webstore.customer_account a USING(makh) JOIN public.khachhang k USING(makh)
            WHERE r.tokenhash=@hash AND r.usedatutc IS NULL AND r.expiresatutc>CURRENT_TIMESTAMP
            AND a.trangthai AND k.trangthai FOR UPDATE OF a,r
            """, ("hash", Hash(token))))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) throw new CustomerRequestException(400, "Liên kết không hợp lệ hoặc đã hết hạn.");
            customer = r.GetString(0); version = r.GetString(1); current = r.GetString(2);
        }
        if (CustomerSessions.Version(current) != version) throw new CustomerRequestException(400, "Liên kết không còn hiệu lực.");
        var nextHash = hasher.HashPassword(new TaiKhoanKhachHang(), password);
        await using (var cmd = SupabaseCustomerFeaturesService.Command(c, tx, """
            UPDATE webstore.customer_account SET matkhauhash=@password WHERE makh=@kh;
            UPDATE webstore.password_reset SET usedatutc=CURRENT_TIMESTAMP WHERE makh=@kh AND usedatutc IS NULL
            """, ("password", nextHash), ("kh", customer)))
            await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }
}
