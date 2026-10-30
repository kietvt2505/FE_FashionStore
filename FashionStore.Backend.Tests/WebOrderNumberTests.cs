using FashionStore.Backend.Services;
using Xunit;
namespace FashionStore.Backend.Tests;
public sealed class WebOrderNumberTests
{
    [Theory]
    [InlineData(1,"WEB-000001")]
    [InlineData(42,"WEB-000042")]
    [InlineData(999999,"WEB-999999")]
    [InlineData(1000000,"WEB-1000000")]
    [InlineData(long.MaxValue,"WEB-9223372036854775807")]
    public void UsesReadableCodesWithoutTruncatingLargeNumbers(long number,string expected) =>
        Assert.Equal(expected,WebOrderNumber.Format(number));
}
