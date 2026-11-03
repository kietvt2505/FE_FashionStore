using FashionStore.Backend.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;

namespace FashionStore.Backend.Services;

public sealed class CustomerPasswordVerifier
{
    private readonly IPasswordHasher<TaiKhoanKhachHang> hasher;
    private readonly TaiKhoanKhachHang dummy = new();
    public CustomerPasswordVerifier(IPasswordHasher<TaiKhoanKhachHang> hasher)
    {
        this.hasher = hasher;
        dummy.MatKhauHash = hasher.HashPassword(dummy, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }
    public PasswordVerificationResult Verify(TaiKhoanKhachHang? account, string password, bool allowLegacyPlaintext = false)
    {
        var selected = account ?? dummy;
        PasswordVerificationResult result;
        try { result=hasher.VerifyHashedPassword(selected, selected.MatKhauHash, password); }
        catch (Exception e) when (e is FormatException or ArgumentException) { result=PasswordVerificationResult.Failed; }
        if(result!=PasswordVerificationResult.Failed || !allowLegacyPlaintext || account==null || !IsLegacyPlaintext(account.MatKhauHash))return result;
        // The unchanged partner form writes plaintext. Upgrade only a matching legacy value;
        // never accept a stored Identity/other encoded hash itself as the user's password.
        hasher.VerifyHashedPassword(dummy,dummy.MatKhauHash,password);
        var expected=SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(account.MatKhauHash));
        var actual=SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password));
        return CryptographicOperations.FixedTimeEquals(expected,actual)
            ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Failed;
    }
    private static bool IsLegacyPlaintext(string? value)
    {
        if(string.IsNullOrEmpty(value)||value.Length>200||value.StartsWith('$')||value.StartsWith("AQAAAA",StringComparison.Ordinal)||value.StartsWith("AAAA",StringComparison.Ordinal))return false;
        if(value.Length is 32 or 40 or 64 or 128 && value.All(Uri.IsHexDigit))return false;
        try { var bytes=Convert.FromBase64String(value);if(bytes.Length>0&&(bytes[0] is 0 or 1 || value.Length>=60))return false; }catch(FormatException) { }
        return true;
    }
}
