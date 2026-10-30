using FashionStore.Backend.Services;
using Xunit;
namespace FashionStore.Backend.Tests;
public sealed class MembershipPricingTests
{
    [Theory]
    [InlineData(0,"Bạc",0)]
    [InlineData(5000000,"Bạc",0)]
    [InlineData(5000001,"Vàng",5)]
    [InlineData(15000000,"Vàng",5)]
    [InlineData(15000001,"Kim Cương",10)]
    public void MatchesStrictPosSpendingThresholds(decimal spent,string rank,int rate)
    {
        var result=SupabaseCheckoutPricing.Membership(spent);
        Assert.Equal(rank,result.Rank);Assert.Equal(rate,result.Rate);
    }
}
