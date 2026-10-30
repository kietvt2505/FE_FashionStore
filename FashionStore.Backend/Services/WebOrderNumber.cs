using System.Globalization;

namespace FashionStore.Backend.Services;

internal static class WebOrderNumber
{
    internal static string Format(long number)
    {
        if(number<1)throw new ArgumentOutOfRangeException(nameof(number));
        // D6 is a minimum width: never truncate when the sequence exceeds 999999.
        return "WEB-"+number.ToString("D6",CultureInfo.InvariantCulture);
    }
}
