using FashionStore.Backend.Services;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class OrderCancellationPolicyTests
{
    [Theory]
    [InlineData("MOI","CHO_XAC_NHAN","CHUA_THANH_TOAN",true)]
    [InlineData("XAC_NHAN","Đã xác nhận","CHUA_THANH_TOAN",true)]
    [InlineData("XAC_NHAN","CHUA_GIAO","THAT_BAI",true)]
    [InlineData("XAC_NHAN","DANG_CHUAN_BI","CHUA_THANH_TOAN",false)]
    [InlineData("XAC_NHAN","Chờ đơn vị vận chuyển tới lấy hàng","CHUA_THANH_TOAN",false)]
    [InlineData("DANG_XU_LY","CHO_XAC_NHAN","CHUA_THANH_TOAN",false)]
    [InlineData("HUY","CHO_XAC_NHAN","CHUA_THANH_TOAN",false)]
    [InlineData("HOAN_TAT","Đã xác nhận","CHUA_THANH_TOAN",false)]
    [InlineData("XAC_NHAN","Đã xác nhận","DA_THANH_TOAN",false)]
    [InlineData("MOI","Trạng thái chưa ánh xạ","CHUA_THANH_TOAN",false)]
    public void LegacyInvoiceStatusCannotOverrideShippingOrPaymentRestrictions(string status,string shipping,string payment,bool allowed)
    {
        Assert.Equal(allowed,OrderCancellationPolicy.AllowsLegacyOrder(status,shipping,payment));
    }
}
