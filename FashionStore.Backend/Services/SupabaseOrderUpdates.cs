using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Middleware;
using Npgsql;
namespace FashionStore.Backend.Services;
public sealed partial class SupabaseOrderService {
 private sealed record UpdateState(string Kind,string Shipping,string Payment,string Method,decimal Received,bool Delivered,string Source,string RowVersion,bool HasAttempt,decimal Total){
 public bool CanDelivery=>Source=="WEB"&&!Delivered&&Kind!="WEB_HUY"&&OrderCancellationPolicy.AllowsShippingStage(Shipping);
 public bool CanPayment=>CanDelivery&&Payment=="CHUA_THANH_TOAN"&&Received==0&&!HasAttempt;
 public bool CanCancel=>CanPayment;
 public string Version=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RowVersion+":"+HasAttempt)));
 }
 private static async Task<UpdateState?> ReadUpdateState(NpgsqlConnection c,NpgsqlTransaction tx,string customer,string id,bool locking,CancellationToken ct){
 await using var cmd=Sql(c,tx,"SELECT h.loaihoadon,COALESCE(h.trangthaigiaohang,''),h.trangthaithanhtoan,h.phuongthucthanhtoan,h.tienkhachdua,h.ngaygiaothanhcong IS NOT NULL,h.nguondon,h.xmin::text,EXISTS(SELECT 1 FROM public.giaodichthanhtoanqr q WHERE q.sohd=h.sohd),h.khachphaitra FROM public.hoadon h WHERE h.sohd=@id AND h.makh=@kh AND h.nguondon='WEB'"+(locking?" FOR UPDATE OF h":""),("id",id),("kh",customer));
 await using var r=await cmd.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetDecimal(4),r.GetBoolean(5),r.GetString(6),r.GetString(7),r.GetBoolean(8),r.GetDecimal(9)):null;
 }
 private async Task<OrderDetails> WithUpdateCapabilities(NpgsqlConnection c,NpgsqlTransaction tx,OrderDetails order,string customer,CancellationToken ct){
 var s=await ReadUpdateState(c,tx,customer,order.OrderId,false,ct);return order with{CanCancel=s?.CanCancel==true,CanUpdateDelivery=s?.CanDelivery==true,CanUpdatePayment=s?.CanPayment==true,EditVersion=s?.Version,SupportsBankTransfer=mode.BankTransferEnabled,CancellationReason=order.Status=="HUY"?order.Note:null,PaymentUpdateNotice=s?.HasAttempt==true?"Đơn đã có giao dịch QR. Vui lòng liên hệ cửa hàng.":null};
 }
 public async Task UpdateInformationAsync(string customer,string id,UpdateOrderRequest input,CancellationToken ct){
 if(input.Section is not ("delivery" or "payment"))throw new CustomerRequestException(400,"Yêu cầu không hợp lệ.");
 if(input.Section=="delivery"&&(string.IsNullOrWhiteSpace(input.Recipient)||input.Recipient.Length>150||string.IsNullOrWhiteSpace(input.Address)||input.Address.Length is <10 or >300||!Regex.IsMatch(input.Phone??"",@"^\+?[0-9]{9,15}$")))throw new CustomerRequestException(400,"Thông tin người nhận không hợp lệ.");
 await using var c=Connection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
 await using(var invoice=Sql(c,tx,"SELECT sohd FROM public.hoadon WHERE sohd=@id AND makh=@kh AND nguondon='WEB' FOR UPDATE",("id",id),("kh",customer)))if(await invoice.ExecuteScalarAsync(ct)==null)throw new CustomerRequestException(404,"Không tìm thấy đơn hàng.");
 var s=await ReadUpdateState(c,tx,customer,id,true,ct)!;if(s==null||s.Version!=input.Version)throw new CustomerRequestException(409,"Thông tin đơn đã thay đổi. Vui lòng tải lại.");
 if(input.Section=="delivery"){if(!s.CanDelivery)throw new CustomerRequestException(409,"Đơn không còn được cập nhật địa chỉ.");await Execute(c,tx,"UPDATE public.hoadon SET tennguoinhan=@name,sodienthoai_kh=@phone,diachigiao=@address WHERE sohd=@id",ct,("id",id),("name",input.Recipient!.Trim()),("phone",input.Phone!.Trim()),("address",input.Address!.Trim()));}
 else{if(!s.CanPayment)throw new CustomerRequestException(409,"Đơn không còn được đổi phương thức thanh toán.");var method=input.PaymentMethod;
 if(method is not ("COD" or "CHUYEN_KHOAN")||method=="CHUYEN_KHOAN"&&!mode.BankTransferEnabled)throw new CustomerRequestException(409,"Phương thức chưa khả dụng.");
 await Execute(c,tx,"UPDATE public.hoadon SET phuongthucthanhtoan=@method,loaihoadon=@kind,trangthaigiaohang=@shipping,tiencod=@cod WHERE sohd=@id",ct,("id",id),("method",method),("kind",method=="COD"?"COD":"WEB_CHO_THANH_TOAN"),("shipping",method=="COD"?"Chờ xác nhận":"Chờ xác nhận thanh toán"),("cod",method=="COD"?s.Total:0m));}
 await tx.CommitAsync(ct);
 }
}