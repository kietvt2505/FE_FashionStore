using System.Data;
using System.Security.Cryptography;
using System.Text;
using FashionStore.Backend.Data;
using FashionStore.Backend.Middleware;
using Npgsql;
namespace FashionStore.Backend.Services;
public sealed class SupabaseCustomerFeaturesService(DatabaseMode mode){
 public NpgsqlConnection Connection()=>new(mode.SupabaseConnection);
 internal static NpgsqlCommand Command(NpgsqlConnection c,NpgsqlTransaction? tx,string text,params (string,object?)[] args)=>SupabaseOrderService.Sql(c,tx,text,args);
 private async Task<List<Dictionary<string,object?>>> Query(string text,CancellationToken ct,params (string,object?)[] args){
 await using var c=Connection();await c.OpenAsync(ct);await using var cmd=Command(c,null,text,args);await using var r=await cmd.ExecuteReaderAsync(ct);var list=new List<Dictionary<string,object?>>();while(await r.ReadAsync(ct)){var row=new Dictionary<string,object?>();for(var i=0;i<r.FieldCount;i++)row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i);list.Add(row);}return list;
 }
 private static CustomerRequestException Deferred()=>new(410,"Chức năng này đang tạm ngưng.");
 public Task<List<Dictionary<string,object?>>> Wishlist(string kh,CancellationToken ct)=>throw Deferred();
 public Task SaveWishlist(string kh,string id,bool remove,CancellationToken ct)=>throw Deferred();
 public Task<List<Dictionary<string,object?>>> Reviews(string id,int page,CancellationToken ct)=>throw Deferred();
 public Task<List<Dictionary<string,object?>>> MyReviews(string kh,string product,int page,CancellationToken ct)=>throw Deferred();
 public Task<List<Dictionary<string,object?>>> ReviewOrders(string kh,string product,CancellationToken ct)=>throw Deferred();
 public Task Review(string kh,ReviewInput input,CancellationToken ct)=>throw Deferred();
 public Task<List<Dictionary<string,object?>>> Addresses(string kh,CancellationToken ct)=>Query("SELECT 0::bigint AS \"Id\",hoten AS \"Recipient\",sodienthoai AS \"Phone\",diachi AS \"Address\" FROM public.khachhang WHERE makh=@kh AND diachi IS NOT NULL",ct,("kh",kh));
 public async Task SaveAddress(string kh,AddressInput input,CancellationToken ct){await using var c=Connection();await c.OpenAsync(ct);await using var cmd=Command(c,null,"UPDATE public.khachhang SET diachi=@address WHERE makh=@kh AND trangthai",("kh",kh),("address",input.Address.Trim()));await cmd.ExecuteNonQueryAsync(ct);}
 public Task UpdateAddress(string kh,long id,AddressInput input,CancellationToken ct)=>id==0?SaveAddress(kh,input,ct):throw new CustomerRequestException(404,"Không tìm thấy địa chỉ.");
 public async Task RemoveAddress(string kh,long id,CancellationToken ct){if(id!=0)throw new CustomerRequestException(404,"Không tìm thấy địa chỉ.");await using var c=Connection();await c.OpenAsync(ct);await using var cmd=Command(c,null,"UPDATE public.khachhang SET diachi=NULL WHERE makh=@kh",("kh",kh));await cmd.ExecuteNonQueryAsync(ct);}
 public Task<List<Dictionary<string,object?>>> Promotions(CancellationToken ct)=>Query("""
 SELECT k.makm AS "MaKM",k.tenchuongtrinh AS "TenChuongTrinh",k.loaikm AS "LoaiKM",k.giatrigiam AS "GiaTriGiam",COALESCE(k.giatridonhangtoithieu,0) AS "GiaTriDonHangToiThieu",
 k.ngayketthuc AS "NgayKetThuc",NULL::integer AS "SoLuotToiDa",0 AS "SoLuotDaDung",EXISTS(SELECT 1 FROM public.khuyenmaisanpham p WHERE p.makm=k.makm) AS "ProductPromotion",
 NOT EXISTS(SELECT 1 FROM public.khuyenmaisanpham p WHERE p.makm=k.makm) AS "CheckoutSupported" FROM public.khuyenmai k WHERE k.trangthai AND CURRENT_DATE BETWEEN k.ngaybatdau AND k.ngayketthuc
 """,ct);
 // The schema stores current points, not a points ledger.
 public Task<List<Dictionary<string,object?>>> Points(string kh,int page,CancellationToken ct)=>Task.FromResult(new List<Dictionary<string,object?>>());
 public Task<List<Dictionary<string,object?>>> History(string kh,string id,CancellationToken ct)=>Query("""
 SELECT 'DON_HANG' AS "LoaiTrangThai",'MOI' AS "TrangThaiMoi",h.ngaylap AS "ThoiDiem",'Đơn hàng đã được ghi nhận' AS "GhiChu" FROM public.hoadon h WHERE h.makh=@kh AND h.sohd=@id AND h.nguondon='WEB'
 UNION ALL SELECT 'GIAO_HANG','DA_GIAO',h.ngaygiaothanhcong,'Cửa hàng ghi nhận giao hàng thành công' FROM public.hoadon h WHERE h.makh=@kh AND h.sohd=@id AND h.nguondon='WEB' AND h.ngaygiaothanhcong IS NOT NULL ORDER BY "ThoiDiem"
 """,ct,("kh",kh),("id",id));
 public Task<List<Dictionary<string,object?>>> Returns(string kh,int page,CancellationToken ct)=>Query("""
 SELECT p.sophieudt AS "SoPhieuDT",p.sohd AS "SoHD",p.ngaylap AS "NgayLap",p.hinhthucxuly AS "HinhThucXuLy",p.trangthai AS "TrangThai",
 (SELECT string_agg(d.lydotra,'; ') FROM public.chitietdoitra d WHERE d.sophieudt=p.sophieudt) AS "LyDo",p.lydotuchoi AS "LyDoTuChoi"
 FROM public.phieudoitra p JOIN public.hoadon h USING(sohd) WHERE h.makh=@kh AND p.nguondon='WEB' ORDER BY p.ngaylap DESC,p.sophieudt LIMIT 20 OFFSET @skip
 """,ct,("kh",kh),("skip",(page-1)*20));
 public async Task CreateReturn(string kh,ReturnInput input,CancellationToken ct){
 if(input.RequestId==Guid.Empty||input.Quantity<1||input.Kind is not ("DOI" or "TRA")||string.IsNullOrWhiteSpace(input.Reason))throw new CustomerRequestException(400,"Yêu cầu đổi trả không hợp lệ.");
 var id="WR"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kh+":"+input.RequestId.ToString("D"))))[..28];
 var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(input))));
 await using var c=Connection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
 await using(var owner=Command(c,tx,"SELECT sohd FROM public.hoadon WHERE sohd=@hd AND makh=@kh AND nguondon='WEB' AND trangthaigiaohang='Hàng giao thành công' AND trangthaithanhtoan='DA_THANH_TOAN' AND ngaygiaothanhcong BETWEEN LOCALTIMESTAMP-INTERVAL '15 days' AND LOCALTIMESTAMP FOR UPDATE",("hd",input.OrderId),("kh",kh)))if(await owner.ExecuteScalarAsync(ct)==null)throw new CustomerRequestException(409,"Đơn chưa nhận hàng, quá hạn 15 ngày hoặc không thuộc tài khoản.");
 await using(var old=Command(c,tx,"SELECT dauvantayyeucau FROM public.phieudoitra WHERE sophieudt=@id",("id",id))){var value=await old.ExecuteScalarAsync(ct);if(value!=null){if(value as string!=hash)throw new CustomerRequestException(409,"Mã yêu cầu đã dùng với thông tin khác.");await tx.CommitAsync(ct);return;}}
 decimal price;
 await using(var line=Command(c,tx,"""
 SELECT d.soluong-COALESCE((SELECT sum(x.soluong) FROM public.chitietdoitra x JOIN public.phieudoitra p USING(sophieudt)
 WHERE p.sohd=d.sohd AND x.masku=d.masku AND p.trangthai<>'TU_CHOI'),0),
 COALESCE(d.giatrithuctra,d.thanhtien)/d.soluong FROM public.chitiethoadon d WHERE d.sohd=@hd AND d.masku=@sku
 """,("hd",input.OrderId),("sku",input.Sku)))await using(var r=await line.ExecuteReaderAsync(ct)){if(!await r.ReadAsync(ct)||r.GetInt64(0)<input.Quantity)throw new CustomerRequestException(409,"Số lượng đổi trả vượt số lượng còn hợp lệ.");price=r.GetDecimal(1);}
 if(input.Kind=="DOI"){await using var target=Command(c,tx,"SELECT 1 FROM public.sanpham p JOIN public.loaisanpham l USING(maloai) WHERE p.masku=@sku AND p.trangthai AND l.trangthai AND p.soluongtonkho>=@qty FOR SHARE OF p,l",("sku",input.NewSku??""),("qty",input.Quantity));if(await target.ExecuteScalarAsync(ct)==null)throw new CustomerRequestException(409,"Phân loại đổi không khả dụng.");}
 await SupabaseOrderService.Execute(c,tx,"""
 INSERT INTO public.phieudoitra(sophieudt,ngaylap,hinhthucxuly,sotienhoan,sohd,nguondon,giatrihangnhanlai,giatrihangdoimoi,sotienthuthem,trangthai,mayeucau,dauvantayyeucau,anhminhchung)
 VALUES(@id,LOCALTIMESTAMP,@kind,0,@hd,'WEB',0,0,0,'CHO_DUYET',@request,@hash,'[]'::jsonb);
 INSERT INTO public.chitietdoitra(sophieudt,masku,soluong,loaigiaodich,tinhtrang,lydotra,maskudoi,soluongdoi,dongianhanlai,dongiadoi,thanhtiennhanlai,thanhtiendoi)
 VALUES(@id,@sku,@qty,@kind,'CHUA_KIEM_TRA',@reason,@newsku,@newqty,@price,0,@amount,0)
 """,ct,("id",id),("kind",input.Kind),("hd",input.OrderId),("request",input.RequestId.ToString("D")),("hash",hash),("sku",input.Sku),("qty",input.Quantity),("reason",input.Reason.Trim()),("newsku",input.Kind=="DOI"?input.NewSku:null),("newqty",input.Kind=="DOI"?input.Quantity:0),("price",price),("amount",price*input.Quantity));await tx.CommitAsync(ct);
 }
}