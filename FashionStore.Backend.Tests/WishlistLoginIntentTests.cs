using FashionStore.Web.Helpers;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class WishlistLoginIntentTests
{
    private static readonly DateTimeOffset Now=new(2026,10,31,3,0,0,TimeSpan.Zero);

    [Fact]
    public void ValidProductSurvivesLoginRetryWithinTheSameVisit()
    {
        var intent=WishlistLoginIntent.Create(" SP-002 ",Now)!;
        Assert.Equal("SP-002",intent.ProductId);
        Assert.True(intent.IsCurrent(Now));
        Assert.True(intent.IsCurrent(Now.AddMinutes(29)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("123456789012345678901")]
    [InlineData("SP-002\nNEXT")]
    [InlineData("../SP-002")]
    [InlineData("SP\\002")]
    public void InvalidProductsCannotBeQueuedForAfterLogin(string? product)
    {
        Assert.Null(WishlistLoginIntent.Create(product!,Now));
    }

    [Fact]
    public void AbandonedAndFutureIntentsCannotBeResumed()
    {
        var intent=WishlistLoginIntent.Create("SP-002",Now)!;
        Assert.False(intent.IsCurrent(Now.AddMinutes(30)));
        Assert.False(intent.IsCurrent(Now.AddSeconds(-1)));
        Assert.False(new WishlistLoginIntent().IsCurrent(Now));
    }
}
