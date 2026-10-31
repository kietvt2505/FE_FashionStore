using FashionStore.Web.Helpers;
using FashionStore.Web.Models;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class ProductReviewEligibilityTests
{
    [Theory]
    [InlineData("MOI","Chờ xác nhận","DA_THANH_TOAN",false)]
    [InlineData("XAC_NHAN","Vừa xác nhận đơn","DA_THANH_TOAN",false)]
    [InlineData("HOAN_TAT","Hàng đang được vận chuyển","DA_THANH_TOAN",false)]
    [InlineData("HOAN_TAT","Shipper đã lấy hàng","DA_THANH_TOAN",false)]
    [InlineData("HOAN_TAT","UNKNOWN","DA_THANH_TOAN",false)]
    [InlineData("HUY","Hàng giao thành công","DA_THANH_TOAN",false)]
    [InlineData("HOAN_TAT","Hàng giao thành công","CHUA_THANH_TOAN",false)]
    [InlineData("HOAN_TAT","Hàng giao thành công","HOAN_TIEN",false)]
    [InlineData("HOAN_TAT","Hàng giao thành công","DA_THANH_TOAN",true)]
    [InlineData("HOAN_TAT","DA_GIAO","DA_THANH_TOAN",true)]
    public void ReviewLinkRequiresExplicitSuccessfulDelivery(string status,string shipping,string payment,bool allowed)
    {
        Assert.Equal(allowed,CustomerViewHelper.CanReviewOrder(new OrderDetailsModel
            { Status=status,ShippingStatus=shipping,PaymentStatus=payment }));
    }
}
