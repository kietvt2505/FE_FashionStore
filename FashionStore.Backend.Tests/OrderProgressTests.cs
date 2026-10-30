using FashionStore.Web.Helpers;
using FashionStore.Web.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class OrderProgressTests
{
    [Theory]
    [InlineData("Chờ xác nhận thanh toán",0)]
    [InlineData("Vừa xác nhận đơn",1)]
    [InlineData("DANG_CHUAN_BI",1)]
    [InlineData("Chờ đơn vị vận chuyển tới lấy hàng",1)]
    [InlineData("Shipper đã lấy hàng",2)]
    [InlineData("Hàng đang được vận chuyển",3)]
    [InlineData("Hàng giao thành công",4)]
    public void SharedDatabaseStatesHighlightOnlyTheActualMilestone(string shipping,int current)
    {
        var progress=CustomerViewHelper.OrderProgress(new OrderDetailsModel{Status="DANG_XU_LY",ShippingStatus=shipping});
        Assert.Equal(5,progress.Steps.Count);
        Assert.Single(progress.Steps,s=>s.State=="current");
        Assert.Equal("current",progress.Steps[current].State);
        Assert.All(progress.Steps.Take(current),s=>Assert.Equal("completed",s.State));
        Assert.All(progress.Steps.Skip(current+1),s=>Assert.Equal("upcoming",s.State));
    }

    [Theory]
    [InlineData("HUY","Shipper đã lấy hàng")]
    [InlineData("CAN_HO_TRO","Chờ xác nhận thanh toán")]
    [InlineData("DANG_XU_LY","HOAN")]
    [InlineData("DANG_XU_LY","Trạng thái mới chưa ánh xạ")]
    public void InterruptedOrUnknownOrdersDoNotClaimToBeDelivering(string status,string shipping)
    {
        var result=CustomerViewHelper.OrderProgress(new OrderDetailsModel{Status=status,ShippingStatus=shipping});
        Assert.NotEmpty(result.Notice);
        Assert.All(result.Steps,s=>Assert.Equal("paused",s.State));
    }

    [Fact]
    public void ShowsRecordedDatesWithoutInventingMissingOrFutureDates()
    {
        var created=new DateTime(2026,10,28,9,0,0,DateTimeKind.Utc);
        var history=JArray.Parse("""
            [{"LoaiTrangThai":"DON_HANG","TrangThaiMoi":"XAC_NHAN","ThoiDiem":"2026-10-28T09:01:00Z"},
             {"LoaiTrangThai":"THANH_TOAN","TrangThaiMoi":"DA_GIAO","ThoiDiem":"2026-10-28T09:02:00Z"},
             {"LoaiTrangThai":"GIAO_HANG","TrangThaiMoi":"DA_GIAO","ThoiDiem":"2026-10-29T09:00:00Z"}]
            """);
        var result=CustomerViewHelper.OrderProgress(new OrderDetailsModel{CreatedAt=created,Status="XAC_NHAN",ShippingStatus="Vừa xác nhận đơn"},history);
        Assert.Equal(created,result.Steps[0].OccurredAt);
        Assert.Equal("28/10/2026 16:01",CustomerViewHelper.VietnamDate(result.Steps[1].OccurredAt!.Value));
        Assert.All(result.Steps.Skip(2),s=>Assert.Null(s.OccurredAt));
        var withoutHistory=CustomerViewHelper.OrderProgress(new OrderDetailsModel{CreatedAt=created,ShippingStatus="Hàng đang được vận chuyển"});
        Assert.Null(withoutHistory.Steps[3].OccurredAt);
    }

    [Theory]
    [InlineData("COD","Đang chờ xác nhận","Đơn đã được xác nhận")]
    [InlineData("PAYOS","Đang chờ xác nhận thanh toán","Đơn đã được xác nhận (Thanh toán thành công)")]
    [InlineData("VISA","Đang chờ xác nhận thanh toán","Đơn đã được xác nhận (Thanh toán thành công)")]
    [InlineData("ZALOPAY","Đang chờ xác nhận thanh toán","Đơn đã được xác nhận (Thanh toán thành công)")]
    public void PaymentTypeDeterminesConfirmationLabels(string method,string first,string confirmed)
    {
        var progress=CustomerViewHelper.OrderProgress(new OrderDetailsModel{PaymentMethod=method,PaymentStatus="DA_THANH_TOAN",ShippingStatus="Vừa xác nhận đơn"});
        Assert.Equal(first,progress.Steps[0].Label);Assert.Equal(confirmed,progress.Steps[1].Label);
        Assert.Equal("Đơn vị vận chuyển lấy hàng thành công",progress.Steps[2].Label);
        Assert.Equal("Đơn vị vận chuyển đang giao hàng",progress.Steps[3].Label);
        Assert.Equal("Giao hàng thành công",progress.Steps[4].Label);
    }
}
