using FashionStore.Backend.Models;
using FashionStore.Backend.Services;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class PasswordCompatibilityTests
{
    private readonly CustomerPasswordVerifier verifier=new(new PasswordHasher<TaiKhoanKhachHang>());
    [Fact]
    public void PartnerPasswordRequiresExplicitLegacyModeAndIsMarkedForUpgrade()
    {
        var account=new TaiKhoanKhachHang{MatKhauHash="Form password 123!"};
        Assert.Equal(PasswordVerificationResult.Failed,verifier.Verify(account,"Form password 123!"));
        Assert.Equal(PasswordVerificationResult.Failed,verifier.Verify(account,"Wrong password 123!",true));
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded,verifier.Verify(account,"Form password 123!",true));
        Assert.Equal(PasswordVerificationResult.Failed,verifier.Verify(null,"Form password 123!",true));
    }
    [Theory]
    [InlineData("AQAAAA-malformed-identity")]
    [InlineData("$2a$12$storedEncodedPassword")]
    [InlineData("5f4dcc3b5aa765d61d8327deb882cf99")]
    public void StoredEncodedValueCannotBeUsedAsThePassword(string encoded)
    {
        Assert.Equal(PasswordVerificationResult.Failed,verifier.Verify(new(){MatKhauHash=encoded},encoded,true));
    }
    [Fact]
    public void IdentityHashContinuesToVerifyWithoutAcceptingTheHashItself()
    {
        var account=new TaiKhoanKhachHang();var hasher=new PasswordHasher<TaiKhoanKhachHang>();
        account.MatKhauHash=hasher.HashPassword(account,"Web password 123!");
        Assert.NotEqual(PasswordVerificationResult.Failed,verifier.Verify(account,"Web password 123!",true));
        Assert.Equal(PasswordVerificationResult.Failed,verifier.Verify(account,account.MatKhauHash,true));
    }
}
