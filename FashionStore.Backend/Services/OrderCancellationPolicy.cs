namespace FashionStore.Backend.Services;

internal static class OrderCancellationPolicy
{
    internal const string RejectionMessage = "Chỉ được hủy ở bước Chờ xác nhận hoặc Đã xác nhận. Đơn đã thanh toán cần có thông tin hoàn tiền hợp lệ; từ bước chuẩn bị hoặc chờ lấy hàng, vui lòng liên hệ cửa hàng.";

    // Match the first two delivery milestones, including the partner's existing status names.
    // Unknown states fail closed even when the invoice's overall status is still XAC_NHAN.
    internal static bool AllowsShippingStage(string? shipping) => shipping?.Trim() is
        "MOI" or "CHO_XAC_NHAN" or "CHUA_GIAO" or "Chờ xác nhận" or "Đang chờ xác nhận" or "Chờ xác nhận thanh toán" or
        "XAC_NHAN" or "Đã xác nhận" or "Vừa xác nhận đơn" or "Đơn đã được xác nhận";

    internal static bool AllowsLegacyOrder(string status, string shipping, string payment) =>
        (status is "MOI" or "XAC_NHAN") && AllowsShippingStage(shipping) &&
        (payment is "CHUA_THANH_TOAN" or "THAT_BAI");
}
