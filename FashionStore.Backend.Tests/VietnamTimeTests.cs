using FashionStore.Web.Helpers;
using FashionStore.Web.Models;
using Newtonsoft.Json;
using Xunit;
namespace FashionStore.Backend.Tests;
public sealed class VietnamTimeTests
{
    [Theory]
    [InlineData("2026-10-27T06:24:00Z","27/10/2026 13:24")]
    [InlineData("2026-10-27T20:30:00Z","28/10/2026 03:30")]
    [InlineData("2026-10-27T13:24:00+07:00","27/10/2026 13:24")]
    public void ApiInstantsDisplayInVietnamWithoutDoubleConversion(string input,string expected)
    {
        var order=JsonConvert.DeserializeObject<OrderSummaryModel>("{\"createdAt\":\""+input+"\"}")!;
        Assert.Equal(expected,CustomerViewHelper.VietnamDate(order.CreatedAt));
    }
    [Fact] public void LegacyLocalDatesAreNotShiftedAndUtcDatesRollAcrossMidnight()
    {
        Assert.Equal("27/10/2026 13:24",CustomerViewHelper.VietnamDate(new DateTime(2026,10,27,13,24,0,DateTimeKind.Unspecified)));
        Assert.Equal("28/10/2026",CustomerViewHelper.VietnamDate(new DateTime(2026,10,27,20,0,0,DateTimeKind.Utc),"dd/MM/yyyy"));
    }
}
