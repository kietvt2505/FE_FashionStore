using System.Data;
using System.Security.Cryptography;
using System.Text;
using FashionStore.Backend.Data;
using FashionStore.Backend.Middleware;
using Npgsql;
namespace FashionStore.Backend.Services;
public sealed partial class SupabaseCustomerFeaturesService(DatabaseMode mode){
 public NpgsqlConnection Connection()=>new(mode.SupabaseConnection);
 internal static NpgsqlCommand Command(NpgsqlConnection c,NpgsqlTransaction? tx,string text,params (string,object?)[] args)=>SupabaseOrderService.Sql(c,tx,text,args);
 private async Task<List<Dictionary<string,object?>>> Query(string text,CancellationToken ct,params (string,object?)[] args){
 await using var c=Connection();await c.OpenAsync(ct);await using var cmd=Command(c,null,text,args);await using var r=await cmd.ExecuteReaderAsync(ct);var list=new List<Dictionary<string,object?>>();while(await r.ReadAsync(ct)){var row=new Dictionary<string,object?>();for(var i=0;i<r.FieldCount;i++)row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i);list.Add(row);}return list;
 }
 private static CustomerRequestException Deferred()=>new(410,"Chức năng này đang tạm ngưng.");
 public Task<List<Dictionary<string,object?>>> Wishlist(string kh,CancellationToken ct)=>throw Deferred();
 public Task SaveWishlist(string kh,string id,bool remove,CancellationToken ct)=>throw Deferred();
 public Task<List<Dictionary<string,object?>>> Addresses(string kh,CancellationToken ct)=>Query("SELECT 0::bigint AS \"Id\",hoten AS \"Recipient\",sodienthoai AS \"Phone\",diachi AS \"Address\" FROM public.khachhang WHERE makh=@kh AND diachi IS NOT NULL",ct,("kh",kh));
 public async Task SaveAddress(string kh,AddressInput input,CancellationToken ct){await using var c=Connection();await c.OpenAsync(ct);await using var cmd=Command(c,null,"UPDATE public.khachhang SET diachi=@address WHERE makh=@kh AND trangthai",("kh",kh),("address",input.Address.Trim()));await cmd.ExecuteNonQueryAsync(ct);}
 public Task UpdateAddress(string kh,long id,AddressInput input,CancellationToken ct)=>id==0?SaveAddress(kh,input,ct):throw new CustomerRequestException(404,"Không tìm thấy địa chỉ.");
 public async Task RemoveAddress(string kh,long id,CancellationToken ct){if(id!=0)throw new CustomerRequestException(404,"Không tìm thấy địa chỉ.");await using var c=Connection();await c.OpenAsync(ct);await using var cmd=Command(c,null,"UPDATE public.khachhang SET diachi=NULL WHERE makh=@kh",("kh",kh));await cmd.ExecuteNonQueryAsync(ct);}
 public Task<List<Dictionary<string,object?>>> Promotions(CancellationToken ct)=>Query("""
 SELECT k.makm AS "MaKM",k.tenchuongtrinh AS "TenChuongTrinh",k.loaikm AS "LoaiKM",k.giatrigiam AS "GiaTriGiam",COALESCE(k.giatridonhangtoithieu,0) AS "GiaTriDonHangToiThieu",
 k.ngayketthuc AS "NgayKetThuc",NULL::integer AS "SoLuotToiDa",0 AS "SoLuotDaDung",EXISTS(SELECT 1 FROM public.khuyenmaisanpham p WHERE p.makm=k.makm) AS "ProductPromotion",
 NOT EXISTS(SELECT 1 FROM public.khuyenmaisanpham p WHERE p.makm=k.makm) AS "CheckoutSupported" FROM public.khuyenmai k WHERE k.trangthai AND (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh') BETWEEN k.ngaybatdau AND k.ngayketthuc
 """,ct);
 // The schema stores current points, not a points ledger.
 public Task<List<Dictionary<string,object?>>> Points(string kh,int page,CancellationToken ct)=>Task.FromResult(new List<Dictionary<string,object?>>());
 public Task<List<Dictionary<string,object?>>> History(string kh,string id,CancellationToken ct)=>Query("""
 SELECT 'DON_HANG' AS "LoaiTrangThai",'MOI' AS "TrangThaiMoi",h.ngaylap AS "ThoiDiem",'Đơn hàng đã được ghi nhận' AS "GhiChu" FROM public.hoadon h WHERE h.makh=@kh AND h.sohd=@id
 UNION ALL SELECT 'GIAO_HANG','DA_GIAO',h.ngaygiaothanhcong,'Cửa hàng ghi nhận giao hàng thành công' FROM public.hoadon h WHERE h.makh=@kh AND h.sohd=@id AND h.ngaygiaothanhcong IS NOT NULL ORDER BY "ThoiDiem"
 """,ct,("kh",kh),("id",id));
 public Task<List<Dictionary<string,object?>>> Returns(string kh,int page,CancellationToken ct)=>Query("""
 SELECT p.sophieudt AS "SoPhieuDT",p.sohd AS "SoHD",p.ngaylap AS "NgayLap",p.hinhthucxuly AS "HinhThucXuLy",p.trangthai AS "TrangThai",
 (SELECT string_agg(d.lydotra,'; ') FROM public.chitietdoitra d WHERE d.sophieudt=p.sophieudt) AS "LyDo",p.lydotuchoi AS "LyDoTuChoi",
 p.sotienhoan AS "SoTienHoan",p.sotienthuthem AS "SoTienThuThem",p.tennguoinhandoi AS "NguoiNhan",p.sodienthoaidoi AS "DienThoai",p.diachidoihang AS "DiaChiDoi",
 p.ngayhoantien AS "NgayHoanTien",p.chutaikhoanhoantien AS "ChuTaiKhoan",p.tennganhanghoantien AS "NganHang",
 CASE WHEN p.sotaikhoanhoantien IS NOT NULL THEN '•••• '||right(p.sotaikhoanhoantien,4) END AS "TaiKhoanAn"
 ,(SELECT string_agg(s.tensanpham||' · '||s.mausac||' · '||s.kichco||' × '||d.soluong||CASE WHEN d.maskudoi IS NOT NULL THEN ' → '||COALESCE(n.mausac,'')||' · '||COALESCE(n.kichco,'')||' × '||d.soluongdoi ELSE '' END,'; ' ORDER BY d.masku)
   FROM public.chitietdoitra d JOIN public.sanpham s ON s.masku=d.masku LEFT JOIN public.sanpham n ON n.masku=d.maskudoi WHERE d.sophieudt=p.sophieudt) AS "SanPham",
 (SELECT COALESCE(SUM(COALESCE((to_jsonb(d)->>'soluongdakiemnhan')::int,0)),0) FROM public.chitietdoitra d WHERE d.sophieudt=p.sophieudt) AS "DaKiem",
 (SELECT COALESCE(SUM(d.soluong),0) FROM public.chitietdoitra d WHERE d.sophieudt=p.sophieudt) AS "SoLuongTra"
 FROM public.phieudoitra p JOIN public.hoadon h USING(sohd) WHERE h.makh=@kh AND p.nguondon='WEB' ORDER BY p.ngaylap DESC,p.sophieudt LIMIT 20 OFFSET @skip
 """,ct,("kh",kh),("skip",(page-1)*20));
 public async Task CreateReturn(string kh,ReturnInput input,CancellationToken ct){
 var kind=NativeStoreCodes.ReturnKind(input.Kind);
 if(input.RequestId==Guid.Empty||input.Quantity is <1 or >100000||kind==null||string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Trim().Length is <5 or >500)throw new CustomerRequestException(400,"Yêu cầu đổi trả không hợp lệ.");
 var newQuantity=kind==NativeStoreCodes.Exchange?input.NewQuantity??input.Quantity:0;
 if(kind==NativeStoreCodes.Exchange && newQuantity is <1 or >100000)throw new CustomerRequestException(400,"Số lượng sản phẩm đổi mới không hợp lệ.");
 if(kind==NativeStoreCodes.Exchange && newQuantity!=input.Quantity)throw new CustomerRequestException(409,"Số lượng hàng đổi phải bằng số lượng hàng gửi lại. Vui lòng mua thêm bằng đơn hàng khác.");
 if(input.Recipient?.Length>150||input.Phone?.Length>20||input.ExchangeAddress?.Length>300)throw new CustomerRequestException(400,"Thông tin nhận hàng đổi vượt giới hạn.");
 if(!string.IsNullOrWhiteSpace(input.Phone)&&!System.Text.RegularExpressions.Regex.IsMatch(input.Phone.Trim(),"^0[0-9]{9,10}$"))throw new CustomerRequestException(400,"Số điện thoại nhận hàng đổi không hợp lệ.");
 if(!string.IsNullOrWhiteSpace(input.AccountHolder)||!string.IsNullOrWhiteSpace(input.AccountNumber)||!string.IsNullOrWhiteSpace(input.BankName))ValidateReturnBank(input.AccountHolder,input.AccountNumber,input.BankName);
 var id="WR"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kh+":"+input.RequestId.ToString("D"))))[..28];
 var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(input))));
 await using var c=Connection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
 string recipient,phone,address;
 await using(var owner=Command(c,tx,"SELECT COALESCE(tennguoinhan,''),COALESCE(sodienthoai_kh,''),COALESCE(diachigiao,'') FROM public.hoadon WHERE sohd=@hd AND makh=@kh AND nguondon='WEB' AND trangthaigiaohang='Hàng giao thành công' AND trangthaithanhtoan='DA_THANH_TOAN' AND ngaygiaothanhcong BETWEEN LOCALTIMESTAMP-INTERVAL '15 days' AND LOCALTIMESTAMP FOR UPDATE",("hd",input.OrderId),("kh",kh)))
 {await using var r=await owner.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CustomerRequestException(409,"Đơn chưa nhận hàng, quá hạn 15 ngày hoặc không thuộc tài khoản.");recipient=input.Recipient?.Trim()??r.GetString(0);phone=input.Phone?.Trim()??r.GetString(1);address=input.ExchangeAddress?.Trim()??r.GetString(2);}
 await using(var old=Command(c,tx,"SELECT dauvantayyeucau FROM public.phieudoitra WHERE sophieudt=@id",("id",id))){var value=await old.ExecuteScalarAsync(ct);if(value!=null){if(value as string!=hash)throw new CustomerRequestException(409,"Mã yêu cầu đã dùng với thông tin khác.");await tx.CommitAsync(ct);return;}}

 await using(var line=Command(c,tx,"""
 SELECT d.soluong-COALESCE((SELECT sum(x.soluong) FROM public.chitietdoitra x JOIN public.phieudoitra p USING(sophieudt)
 WHERE p.sohd=d.sohd AND x.masku=d.masku AND p.trangthai<>'TU_CHOI'),0)
 FROM public.chitiethoadon d WHERE d.sohd=@hd AND d.masku=@sku
 """,("hd",input.OrderId),("sku",input.Sku)))await using(var r=await line.ExecuteReaderAsync(ct)){if(!await r.ReadAsync(ct)||r.GetInt64(0)<input.Quantity)throw new CustomerRequestException(409,"Số lượng đổi trả vượt số lượng còn hợp lệ.");}
 if(kind==NativeStoreCodes.Exchange){await using var target=Command(c,tx,"SELECT 1 FROM public.sanpham p JOIN public.loaisanpham l USING(maloai) JOIN public.sanpham original ON original.masku=@original AND original.masp=p.masp WHERE p.masku=@sku AND p.trangthai AND l.trangthai AND p.soluongtonkho>=@qty FOR SHARE OF p,l,original",("original",input.Sku),("sku",input.NewSku??input.Sku),("qty",newQuantity));if(await target.ExecuteScalarAsync(ct)==null)throw new CustomerRequestException(409,"Chỉ đổi màu/size của cùng sản phẩm đã mua và phải còn đủ hàng.");}
 await SupabaseOrderService.Execute(c,tx,"""
 INSERT INTO public.phieudoitra(sophieudt,ngaylap,hinhthucxuly,sotienhoan,sohd,nguondon,giatrihangnhanlai,giatrihangdoimoi,sotienthuthem,trangthai,mayeucau,dauvantayyeucau,anhminhchung,tennguoinhandoi,sodienthoaidoi,diachidoihang,chutaikhoanhoantien,sotaikhoanhoantien,tennganhanghoantien)
 VALUES(@id,LOCALTIMESTAMP,@kind,0,@hd,'WEB',0,0,0,'CHO_DUYET',@request,@hash,'[]'::jsonb,@recipient,@phone,@address,@holder,@account,@bank);
 INSERT INTO public.chitietdoitra(sophieudt,masku,soluong,loaigiaodich,tinhtrang,lydotra,maskudoi,soluongdoi,dongianhanlai,dongiadoi,thanhtiennhanlai,thanhtiendoi)
 VALUES(@id,@sku,@qty,@kind,'CHUA_KIEM_TRA',@reason,@newsku,@newqty,@price,0,@amount,0)
 """,ct,("id",id),("kind",kind),("hd",input.OrderId),("request",input.RequestId.ToString("D")),("hash",hash),("sku",input.Sku),("qty",input.Quantity),("reason",input.Reason.Trim()),("newsku",kind==NativeStoreCodes.Exchange?input.NewSku??input.Sku:null),("newqty",newQuantity),("price",0m),("amount",0m),("recipient",recipient),("phone",phone),("address",address),("holder",input.AccountHolder?.Trim()),("account",input.AccountNumber),("bank",input.BankName?.Trim()));await tx.CommitAsync(ct);
 }
}
