using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FashionStore.Web.Models
{
    public sealed class CheckoutViewModel
    {
        [Required(ErrorMessage="Vui lòng nhập {0}."), StringLength(150, MinimumLength = 2, ErrorMessage="{0} vượt giới hạn hoặc chưa đủ độ dài yêu cầu."), Display(Name = "Người nhận")]
        public string Recipient { get; set; }
        [Required(ErrorMessage="Vui lòng nhập {0}."), RegularExpression(@"^\+?[0-9]{9,15}$", ErrorMessage="{0} không đúng định dạng."), Display(Name = "Số điện thoại")]
        public string Phone { get; set; }
        [Required(ErrorMessage="Vui lòng nhập {0}."), StringLength(300, MinimumLength = 10, ErrorMessage="{0} vượt giới hạn hoặc chưa đủ độ dài yêu cầu."), Display(Name = "Địa chỉ giao hàng")]
        public string Address { get; set; }
        [Required(ErrorMessage="Vui lòng nhập {0}."), RegularExpression("^(COD|CHUYEN_KHOAN|VISA|VNPAY|ZALOPAY|PAYOS)$", ErrorMessage="{0} không đúng định dạng."), Display(Name = "Phương thức thanh toán")]
        public string PaymentMethod { get; set; } = "COD";
        [StringLength(500, ErrorMessage="{0} vượt giới hạn hoặc chưa đủ độ dài yêu cầu."), Display(Name = "Ghi chú")]
        public string Note { get; set; }
        [Required(ErrorMessage="Vui lòng nhập {0}.")] public Guid RequestId { get; set; }
        [StringLength(20, ErrorMessage="{0} vượt giới hạn hoặc chưa đủ độ dài yêu cầu."),Display(Name="Mã ưu đãi")] public string Voucher {get;set;}
        public decimal? QuotedDiscount {get;set;}
        public decimal? QuotedTotal {get;set;}
        public decimal QuotedMemberDiscount {get;set;}
        public decimal QuotedVoucherDiscount {get;set;}
        public string AppliedVoucher {get;set;}
        public bool MembershipEnabled {get;set;}
        public string MemberRank {get;set;}
        public int MemberRate {get;set;}
        public int MemberPoints {get;set;}
        public CustomerCart Cart { get; set; }
        public List<OnlinePaymentOptionModel> OnlineMethods {get;set;} = new List<OnlinePaymentOptionModel>();
    }
    public sealed class OnlinePaymentOptionModel {public string Code{get;set;} public string Label{get;set;} public bool Enabled{get;set;}}
    public sealed class PaymentLinkModel {public string Url{get;set;} public string OrderId{get;set;}}
    public sealed class PlacedOrder
    {
        public string OrderId { get; set; }
        public string Status { get; set; }
        public decimal Total { get; set; }
        public string PaymentMethod { get; set; }
    }
    public sealed class OrderSummaryModel
    {
        public string OrderId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; }
        public string ShippingStatus { get; set; }
        public string PaymentStatus { get; set; }
        public string PaymentMethod { get; set; }
        public decimal Total { get; set; }
        public string Recipient { get; set; }
    }
    public sealed class OrderLineModel
    {
        public string ProductId { get; set; }
        public string ImageUrl { get; set; }
        public string Sku { get; set; }
        public string Name { get; set; }
        public string Size { get; set; }
        public string Color { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Total { get; set; }
    }
    public sealed class OrderDetailsModel
    {
        public bool SupportsHistory { get; set; } = true;
        public bool SupportsReturns { get; set; } = true;
        public bool CanCancel { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string CancellationReason {get;set;}
        public OrderRefundModel Refund {get;set;}
        public string OrderId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; }
        public string ShippingStatus { get; set; }
        public string PaymentStatus { get; set; }
        public string PaymentMethod { get; set; }
        public decimal Total { get; set; }
        public string Recipient { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string Note { get; set; }
        public decimal Subtotal {get;set;}
        public decimal Discount {get;set;}
        public string MemberRank {get;set;}
        public int MemberRate {get;set;}
        public int? MemberPoints {get;set;}
        public decimal MemberDiscount {get;set;}
        public decimal VoucherDiscount {get;set;}
        public string Voucher {get;set;}
        public List<OrderLineModel> Items { get; set; } = new List<OrderLineModel>();
    }
    public sealed class OrderRefundModel
    {
        public string Status {get;set;}
        public decimal Amount {get;set;}
        public string AccountHolder {get;set;}
        public string MaskedAccountNumber {get;set;}
        public string BankName {get;set;}
        public DateTime? RefundedAt {get;set;}
    }
    public sealed class CancelOrderViewModel
    {
        public string Id {get;set;}
        [StringLength(500,ErrorMessage="Lý do tối đa 500 ký tự.")]
        public string Reason {get;set;}
        [StringLength(150,ErrorMessage="Tên chủ tài khoản tối đa 150 ký tự.")]
        public string AccountHolder {get;set;}
        [RegularExpression("^[0-9]{6,34}$",ErrorMessage="Số tài khoản gồm 6–34 chữ số, không chứa dấu cách.")]
        public string AccountNumber {get;set;}
        [StringLength(100,ErrorMessage="Tên ngân hàng tối đa 100 ký tự.")]
        public string BankName {get;set;}
        public bool RequiresReason {get;set;}
        public bool RequiresRefund {get;set;}
        public bool RefundAccountOnly {get;set;}
        public bool Open {get;set;}
    }
    public sealed class OrderActionModel
    {
        public string OrderId { get; set; }
        public string Status { get; set; }
        public string Result { get; set; }
    }
}
