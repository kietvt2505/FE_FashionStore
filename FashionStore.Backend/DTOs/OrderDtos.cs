using System.ComponentModel.DataAnnotations;

namespace FashionStore.Backend.DTOs;

public sealed record CheckoutRequest(
    [param: Required, StringLength(150, MinimumLength = 2)] string Recipient,
    [param: Required, RegularExpression(@"^\+?[0-9]{9,15}$")] string Phone,
    [param: Required, StringLength(300, MinimumLength = 10)] string Address,
    [param: Required, RegularExpression("^(COD|CHUYEN_KHOAN|VISA|VNPAY|ZALOPAY|PAYOS)$")] string PaymentMethod,
    [param: StringLength(500)] string? Note,
    [param: Required] Guid RequestId,
    [param: StringLength(20)] string? Voucher = null,
    IReadOnlyList<CartItemRequest>? Items = null);
public sealed record BrowserCheckoutQuote(IReadOnlyList<CartItemRequest> Items,string? Voucher=null);
public sealed record CheckoutQuote(decimal Subtotal, decimal Discount, decimal Total)
{
    public bool MembershipEnabled { get; init; }
    public string? MemberRank { get; init; }
    public int MemberRate { get; init; }
    public int MemberPoints { get; init; }
    public decimal MemberDiscount {get;init;}
    public string? Voucher {get;init;}
    public decimal VoucherDiscount {get;init;}
}

public sealed record PlaceOrderResponse(string OrderId, string Status, decimal Total, string PaymentMethod);
public sealed record OrderSummary(string OrderId, DateTime CreatedAt, string Status, string ShippingStatus,
    string PaymentStatus, string PaymentMethod, decimal Total, string Recipient);
public sealed record OrderLine(string Sku, string Name, string Size, string Color, int Quantity, decimal UnitPrice, decimal Total, string? ImageUrl = null)
{
    public string? ProductId { get; init; }
}
public sealed record OrderDetails(string OrderId, DateTime CreatedAt, string Status, string ShippingStatus,
    string PaymentStatus, string PaymentMethod, decimal Total, string Recipient, string Phone, string Address,
    string? Note, IReadOnlyList<OrderLine> Items, decimal Subtotal=0, decimal Discount=0, string? Voucher=null)
{
    public bool SupportsHistory { get; init; } = true;
    public bool SupportsReturns { get; init; } = true;
    public bool CanCancel { get; init; }
    public bool CanUpdateDelivery { get; init; }
    public bool CanUpdatePayment { get; init; }
    public bool SupportsBankTransfer { get; init; }
    public string? EditVersion { get; init; }
    public string? PaymentUpdateNotice { get; init; }
    public DateTime? CancelledAt { get; init; }
    public string? CancellationReason { get; init; }
    public OrderRefundDetails? Refund { get; init; }
    public string? MemberRank { get; init; }
    public int MemberRate { get; init; }
    public int? MemberPoints { get; init; }
    public decimal MemberDiscount { get; init; }
    public decimal VoucherDiscount {get;init;}
}
public sealed record CancelOrderRequest(
    [param: StringLength(500)] string? Reason = null,
    [param: StringLength(150)] string? AccountHolder = null,
    [param: RegularExpression("^[0-9]{6,34}$")] string? AccountNumber = null,
    [param: StringLength(100)] string? BankName = null);
public sealed record OrderRefundDetails(string Status,decimal Amount,string? AccountHolder,string? MaskedAccountNumber,
    string? BankName,DateTime? RefundedAt);
public sealed record OrderActionResponse(string OrderId, string Status, string Result);

public sealed record UpdateOrderRequest(
    [param: Required, RegularExpression("^(delivery|payment)$")] string Section,
    [param: Required, RegularExpression("^[A-F0-9]{64}$")] string Version,
    [param: StringLength(150)] string? Recipient = null,
    [param: StringLength(20)] string? Phone = null,
    [param: StringLength(300)] string? Address = null,
    [param: RegularExpression("^(COD|CHUYEN_KHOAN|VISA|VNPAY|ZALOPAY|PAYOS)$")] string? PaymentMethod = null);
