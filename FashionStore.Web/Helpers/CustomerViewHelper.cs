using System;
using System.Collections.Generic;
using System.Globalization;
using FashionStore.Web.Models;
using Newtonsoft.Json.Linq;
namespace FashionStore.Web.Helpers {
 public sealed class OrderProgressStep {
  public string Label {get;set;}
  public string State {get;set;}
  public string StateLabel {get;set;}
  public DateTime? OccurredAt {get;set;}
 }
 public sealed class OrderProgressModel {
  public List<OrderProgressStep> Steps {get;set;} = new List<OrderProgressStep>();
  public string Notice {get;set;}
 }
 public static class CustomerViewHelper {
  // Shared database and legacy API names describe the same customer-facing delivery milestones.
  private static int DeliveryStep(string value) {
   switch((value??"").Trim().ToUpperInvariant()) {
    case "MOI":case "CHO_XAC_NHAN":case "CHUA_GIAO":case "CHỜ XÁC NHẬN":case "ĐANG CHỜ XÁC NHẬN":case "CHỜ XÁC NHẬN THANH TOÁN":case "ĐANG CHỜ XÁC NHẬN THANH TOÁN":return 0;
    case "XAC_NHAN":case "ĐÃ XÁC NHẬN":case "VỪA XÁC NHẬN ĐƠN":case "ĐƠN ĐÃ ĐƯỢC XÁC NHẬN":return 1;
    // Preparation is part of the confirmed milestone; cancellation still uses its stricter server policy.
    case "DANG_CHUAN_BI":case "CHO_LAY_HANG":case "ĐANG CHUẨN BỊ":case "CHỜ LẤY HÀNG":case "CHỜ ĐƠN VỊ VẬN CHUYỂN TỚI LẤY HÀNG":case "CHỜ ĐƠN VỊ VẬN CHUYỂN LẤY HÀNG":return 1;
    case "DA_LAY_HANG":case "SHIPPER ĐÃ LẤY HÀNG":case "ĐƠN VỊ VẬN CHUYỂN LẤY HÀNG THÀNH CÔNG":return 2;
    case "DANG_GIAO":case "ĐANG GIAO":case "ĐANG GIAO HÀNG":case "HÀNG ĐANG ĐƯỢC VẬN CHUYỂN":case "ĐƠN VỊ VẬN CHUYỂN ĐANG GIAO HÀNG":return 3;
    case "DA_GIAO":case "ĐÃ GIAO":case "ĐÃ GIAO HÀNG":case "HÀNG GIAO THÀNH CÔNG":case "GIAO HÀNG THÀNH CÔNG":return 4;
    default:return -1;
   }
  }
  public static OrderProgressModel OrderProgress(OrderDetailsModel order,JArray history=null) {
   var result=new OrderProgressModel();
   var current=DeliveryStep(order.ShippingStatus);
   if(current<0) {
    if(order.Status=="MOI")current=0;
    else if(order.Status=="XAC_NHAN")current=1;
    else if(order.Status=="HOAN_TAT")current=4;
   }
   if(order.Status=="HUY") {current=-1;result.Notice="Đơn hàng đã hủy. Lộ trình giao hàng đã dừng.";}
   else if(order.ShippingStatus=="HOAN") {current=-1;result.Notice="Đơn hàng đang được xử lý hoàn trả. Lộ trình giao hàng đã dừng.";}
   else if(order.Status=="CAN_HO_TRO") {current=-1;result.Notice="Đơn hàng cần cửa hàng kiểm tra. Chưa có bước giao hàng hiện tại được xác nhận.";}
   else if(current<0)result.Notice="Cửa hàng chưa cập nhật bước giao hàng hiện tại.";
   var dates=new DateTime?[5];
   dates[0]=order.CreatedAt;
   if(history!=null)foreach(var entry in history) {
    var type=(string)entry["LoaiTrangThai"];
    if(type!="DON_HANG"&&type!="GIAO_HANG")continue;
    var index=DeliveryStep((string)entry["TrangThaiMoi"]);
    DateTime recorded;
    if(index<0||index>current)continue;
    var date=entry["ThoiDiem"];
    // JArray already parses ISO dates; converting its Date token to text loses the UTC kind.
    if(date!=null&&date.Type==JTokenType.Date)recorded=date.Value<DateTime>();
    else if(!DateTime.TryParse((string)date,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out recorded))continue;
    if(!dates[index].HasValue||recorded<dates[index].Value)dates[index]=recorded;
   }
   var labels=DeliveryLabels(order.PaymentMethod,order.PaymentStatus);
   for(var i=0;i<labels.Length;i++)result.Steps.Add(new OrderProgressStep {
    Label=labels[i],State=current<0?"paused":i<current?"completed":i==current?"current":"upcoming",
    StateLabel=current<0?null:i<current?"Đã qua":i==current?(i==4?"Hoàn tất":"Hiện tại"):"Sắp tới",
    // Never invent timestamps for future milestones or milestones with no recorded history.
    OccurredAt=i<=current?dates[i]:null
   });
   return result;
  }
  private static string[] DeliveryLabels(string method,string payment) {
   return new[]{method=="COD"?"Đang chờ xác nhận":"Đang chờ xác nhận thanh toán",
    method=="COD"?"Đơn đã được xác nhận":"Đơn đã được xác nhận (Thanh toán thành công)",
    "Đơn vị vận chuyển lấy hàng thành công","Đơn vị vận chuyển đang giao hàng","Giao hàng thành công"};
  }
  public static string OrderStatus(string status,string shipping,string payment,string method) {
   if(status=="HUY"||status=="CAN_HO_TRO")return Status(status);
   var step=DeliveryStep(shipping);
   if(step<0)step=status=="MOI"?0:status=="XAC_NHAN"?1:status=="HOAN_TAT"?4:-1;
   return step<0?Status(status):DeliveryLabels(method,payment)[step];
  }
  public static bool IsOnlinePayment(string method){return method=="VNPAY"||method=="VISA"||method=="ZALOPAY"||method=="PAYOS";}
  public static string PaymentMethod(string method){switch(method){case "COD":return "Thu hộ COD";case "CHUYEN_KHOAN":return "Chuyển khoản ngân hàng";case "PAYOS":return "Quét mã QR ngân hàng (payOS)";case "VISA":return "Thẻ Visa (qua VNPay)";case "VNPAY":return "VNPay ngân hàng";case "ZALOPAY":return "Ví ZaloPay";default:return "Đang cập nhật";}}
  public static string VietnamDate(DateTime value,string format="dd/MM/yyyy HH:mm") {
   // Naive legacy SQL Server dates are already local. UTC/offset API dates are instants.
   var local=value.Kind==DateTimeKind.Unspecified?value:new DateTimeOffset(value).ToOffset(TimeSpan.FromHours(7)).DateTime;
   return local.ToString(format,CultureInfo.GetCultureInfo("vi-VN"));
  }
  private static readonly IDictionary<string,string> Labels=new Dictionary<string,string>{
   {"Đã nhận tiền - cần cửa hàng xử lý","Đã nhận tiền - cần cửa hàng xử lý"},{"Chờ xác nhận thanh toán","Chờ xác nhận thanh toán"},{"CAN_HO_TRO","Cần liên hệ cửa hàng"},{"CHUA_XAC_MINH","Chưa xác minh"},
   {"Đã hủy","Đã hủy"},{"Vừa xác nhận đơn","Vừa xác nhận đơn"},{"Shipper đã lấy hàng","Shipper đã lấy hàng"},
   {"Hàng đang được vận chuyển","Hàng đang được vận chuyển"},{"Hàng giao thành công","Hàng giao thành công"},
   {"Cần liên hệ cửa hàng","Cần liên hệ cửa hàng"},{"MOI","Đơn mới"},{"XAC_NHAN","Đã xác nhận"},{"DANG_XU_LY","Đang xử lý"},{"HOAN_TAT","Hoàn thành"},{"HUY","Đã hủy"},
   {"CHO_XAC_NHAN","Chờ xác nhận"},{"CHUA_GIAO","Chưa giao"},{"DANG_CHUAN_BI","Đang chuẩn bị"},{"DANG_GIAO","Đang giao"},{"DA_GIAO","Đã giao"},{"HOAN","Hoàn trả"},
   {"CHUA_THANH_TOAN","Chưa thanh toán"},{"DA_THANH_TOAN","Đã thanh toán"},{"HOAN_TIEN","Đã hoàn tiền"},{"HOAN_MOT_PHAN","Hoàn tiền một phần"},{"THAT_BAI","Không thành công"},
   {"CHO_DUYET","Chờ duyệt"},{"DA_DUYET","Đã duyệt"},{"TU_CHOI","Từ chối"},{"TICH","Tích điểm"},{"SU_DUNG","Sử dụng điểm"},{"DIEU_CHINH","Điều chỉnh"},
   {"TRA","Trả hàng"},{"DOI","Đổi hàng"},{"DOI_VA_TRA","Đổi và trả"},{"DON_HANG","Đơn hàng"},{"GIAO_HANG","Giao hàng"},{"THANH_TOAN","Thanh toán"}};
  public static string Status(string value){string text;return value!=null&&Labels.TryGetValue(value,out text)?text:"Đang cập nhật";}
 }
}
