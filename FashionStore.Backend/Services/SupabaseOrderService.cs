using System.Data;
using FashionStore.Backend.Data;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Middleware;
using Npgsql;
namespace FashionStore.Backend.Services;

// The fixed partner invoice and invoice-line tables own all persisted orders.
public sealed partial class SupabaseOrderService(DatabaseMode mode)
{
    private NpgsqlConnection Connection(){if(!mode.OrdersEnabled)throw new CustomerRequestException(503,"Đặt hàng chưa được bật.");return new(mode.SupabaseConnection);}
    internal static NpgsqlCommand Sql(NpgsqlConnection c,NpgsqlTransaction? tx,string text,params (string Name,object? Value)[] args){var cmd=new NpgsqlCommand(text,c,tx){CommandTimeout=20};foreach(var (name,value) in args)cmd.Parameters.AddWithValue(name,value??DBNull.Value);return cmd;}
    internal static async Task Execute(NpgsqlConnection c,NpgsqlTransaction? tx,string text,CancellationToken ct,params (string Name,object? Value)[] args){await using var cmd=Sql(c,tx,text,args);await cmd.ExecuteNonQueryAsync(ct);}
    private const string StatusSql="""
        CASE WHEN h.loaihoadon='WEB_HUY' OR btrim(COALESCE(h.trangthaigiaohang,'')) IN ('Đã hủy','Đã huỷ')
          OR upper(btrim(COALESCE(h.trangthaigiaohang,''))) IN ('HUY','DA_HUY','CANCELLED','CANCELED') THEN 'HUY'
        WHEN h.trangthaigiaohang='Hàng giao thành công' THEN 'HOAN_TAT'
        WHEN upper(h.loaihoadon)='TAI_QUAY' AND h.trangthaithanhtoan='DA_THANH_TOAN' THEN 'HOAN_TAT'
        WHEN h.trangthaigiaohang IN ('Shipper đã lấy hàng','Hàng đang được vận chuyển') THEN 'DANG_XU_LY'
        WHEN h.trangthaigiaohang IN ('Chờ xác nhận','Chờ xác nhận thanh toán','MOI','CHO_XAC_NHAN','CHUA_GIAO','Đang chờ xác nhận') THEN 'MOI' ELSE 'XAC_NHAN' END
        """;
    private static async Task<List<OrderLine>> BrowserLines(NpgsqlConnection c,NpgsqlTransaction tx,IReadOnlyList<CartItemRequest>? items,bool locking,CancellationToken ct){
        if(items==null||items.Count is <1 or >50||items.Any(i=>i==null||string.IsNullOrWhiteSpace(i.Sku)||i.Sku.Length>20||i.Quantity<1))throw new CustomerRequestException(400,"Giỏ hàng không hợp lệ.");
        var result=new List<OrderLine>();
        foreach(var item in items.GroupBy(i=>i.Sku,StringComparer.Ordinal).Select(g=>new{Sku=g.Key,Quantity=g.Sum(i=>(long)i.Quantity)}).OrderBy(i=>i.Sku,StringComparer.Ordinal)){
            await using var cmd=Sql(c,tx,"""
                SELECT p.tensanpham,p.kichco,p.mausac,COALESCE(k.giakhuyenmai,p.dongia),p.soluongtonkho,p.trangthai AND l.trangthai
                FROM public.sanpham p JOIN public.loaisanpham l USING(maloai)
                LEFT JOIN public.sanphamkhuyenmai_hientai k ON k.masku=p.masku WHERE p.masku=@sku
                """+(locking?" FOR UPDATE OF p FOR SHARE OF l":""),("sku",item.Sku));
            await using var r=await cmd.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct)||!r.GetBoolean(5)||item.Quantity>r.GetInt32(4)||r.GetDecimal(3)<=0)throw new CustomerRequestException(409,"Sản phẩm, giá hoặc tồn kho đã thay đổi. Vui lòng kiểm tra giỏ hàng.");
            result.Add(new(item.Sku,r.GetString(0),r.GetString(1),r.GetString(2),(int)item.Quantity,r.GetDecimal(3),r.GetDecimal(3)*item.Quantity));
        }
        return result;
    }
    public Task<CheckoutQuote> QuoteAsync(string customer,string? voucher,CancellationToken ct)=>throw new CustomerRequestException(400,"Vui lòng gửi sản phẩm trong giỏ trên trình duyệt.");
    public async Task<CheckoutQuote> QuoteBrowserAsync(string customer,IReadOnlyList<CartItemRequest> items,string? voucher,CancellationToken ct){
        await using var c=Connection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        var lines=await BrowserLines(c,tx,items,false,ct);var p=await SupabaseCheckoutPricing.Calculate(c,tx,true,customer,voucher,lines.Sum(l=>l.Total),ct);await tx.CommitAsync(ct);
        return new(p.Subtotal,p.Discount,p.Total){MembershipEnabled=true,MemberRank=p.Rank,MemberRate=p.Rate,MemberPoints=p.Points,MemberDiscount=p.MemberDiscount,Voucher=p.Voucher,VoucherDiscount=p.VoucherDiscount,VatAmount=p.VatAmount,VatRate=8,VoucherIgnored=p.VoucherIgnored};
    }
    public async Task<PlaceOrderResponse> PlaceAsync(string customer,CheckoutRequest request,CancellationToken ct){
        if(request.RequestId==Guid.Empty)throw new CustomerRequestException(400,"Mã yêu cầu không hợp lệ.");
        if(request.PaymentMethod is not (NativeStoreCodes.Cod or NativeStoreCodes.Transfer))throw new CustomerRequestException(409,"Website hiện chỉ hỗ trợ COD hoặc chuyển khoản. Thanh toán QR trực tuyến chưa được mở.");
        if(request.Items==null||request.Items.Count is <1 or >50||request.Items.Any(i=>i==null||string.IsNullOrWhiteSpace(i.Sku)||i.Quantity<1))throw new CustomerRequestException(400,"Giỏ hàng không hợp lệ.");
        if(request.PaymentMethod=="CHUYEN_KHOAN"&&!mode.BankTransferEnabled)throw new CustomerRequestException(409,"Chuyển khoản chưa được bật.");
        var fingerprint=NativeOrderRequestFingerprint.Create(customer,request);
        await using var c=Connection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        // Serialize web retries without locking the customer before products. WinForms takes
        // product locks before updating loyalty, so both systems must follow that lock order.
        await Execute(c,tx,"SELECT pg_advisory_xact_lock(19580,hashtext(@kh))",ct,("kh",customer));
        await using(var owner=Sql(c,tx,"SELECT makh FROM public.khachhang WHERE makh=@kh AND trangthai",("kh",customer)))if(await owner.ExecuteScalarAsync(ct)==null)throw new CustomerRequestException(401,"Vui lòng đăng nhập lại.");
        string? previous=null;
        await using(var old=Sql(c,tx,"SELECT sohd,tennguoinhan,sodienthoai_kh,diachigiao,phuongthucthanhtoan,COALESCE(ghichugiaohang,''),makm,dauvantayyeucau FROM public.hoadon WHERE makh=@kh AND (dauvantayyeucau=@legacy OR left(dauvantayyeucau,32)=@prefix)",("kh",customer),("legacy",fingerprint.Legacy),("prefix",fingerprint.Prefix)))
        await using(var r=await old.ExecuteReaderAsync(ct)){if(await r.ReadAsync(ct)){
            previous=r.GetString(0);
            var legacy=r.GetString(7)==fingerprint.Legacy;
            if((!legacy&&r.GetString(7)!=fingerprint.Value)||(legacy&&(r.GetString(1)!=request.Recipient.Trim()||r.GetString(2)!=request.Phone.Trim()||r.GetString(3)!=request.Address.Trim()||r.GetString(4)!=request.PaymentMethod||r.GetString(5)!=(request.Note?.Trim()??"")||(!r.IsDBNull(6)?r.GetString(6):null)!=(string.IsNullOrWhiteSpace(request.Voucher)?null:request.Voucher.Trim().ToUpperInvariant()))))throw new CustomerRequestException(409,"Mã yêu cầu đã được dùng với thông tin khác. Vui lòng tải lại biểu mẫu.");
        }}
        if(previous!=null){
            var expected=request.Items.GroupBy(i=>i.Sku).ToDictionary(g=>g.Key,g=>g.Sum(i=>(long)i.Quantity));var actual=new Dictionary<string,long>();
            await using(var cmd=Sql(c,tx,"SELECT masku,soluong FROM public.chitiethoadon WHERE sohd=@id",("id",previous)))await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))actual[r.GetString(0)]=r.GetInt32(1);
            if(!actual.OrderBy(p=>p.Key).SequenceEqual(expected.OrderBy(p=>p.Key)))throw new CustomerRequestException(409,"Mã yêu cầu đã được dùng cho giỏ hàng khác.");
            await tx.CommitAsync(ct);var existing=await DetailAsync(previous,customer,ct);return new(existing.OrderId,existing.Status,existing.Total,existing.PaymentMethod);
        }
        await using(var emp=Sql(c,tx,"SELECT 1 FROM public.nhanvien WHERE manv=@id AND trangthai FOR SHARE",("id",mode.WebOrderEmployeeId)))if(await emp.ExecuteScalarAsync(ct)==null)throw new CustomerRequestException(503,"Nhân viên tiếp nhận đơn chưa được cấu hình hợp lệ.");
        var lines=await BrowserLines(c,tx,request.Items,true,ct);var p=await SupabaseCheckoutPricing.Calculate(c,tx,true,customer,request.Voucher,lines.Sum(l=>l.Total),ct);
        if(request.ExpectedTotal.HasValue&&request.ExpectedTotal.Value!=p.Total)throw new CustomerRequestException(409,"Giá, ưu đãi hoặc thuế đã thay đổi. Vui lòng kiểm tra tổng thanh toán mới rồi xác nhận lại.");
        if(p.Total<=0||p.Subtotal>9999999999999999m||p.Total>9999999999999999m)throw new CustomerRequestException(400,"Giá trị đơn không hợp lệ.");
        await Execute(c,tx,"SELECT pg_advisory_xact_lock(19579,((CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh')::date-DATE '2000-01-01'))",ct);
        string id;
        await using(var number=Sql(c,tx,"""
            SELECT 'HD'||to_char(CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh','DDMMYYYY')||'-'||lpad(n::text,4,'0')
            FROM generate_series(1,9999) n WHERE NOT EXISTS(SELECT 1 FROM public.hoadon h
            WHERE h.sohd='HD'||to_char(CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh','DDMMYYYY')||'-'||lpad(n::text,4,'0')) ORDER BY n LIMIT 1
            """))id=await number.ExecuteScalarAsync(ct) as string??throw new CustomerRequestException(503,"Hôm nay đã hết mã đơn hàng.");
        await EnsureInventoryContract(c,tx,ct);
        await Execute(c,tx,"""
            INSERT INTO public.hoadon(sohd,ngaylap,loaihoadon,phuongthucthanhtoan,tongcong,chietkhau,thuevat,khachphaitra,tienkhachdua,tienthua,
                sodienthoai_kh,makh,diachigiao,donvivanchuyen,trangthaigiaohang,trangthaithanhtoan,phiship,tiencod,manv,tennguoinhan,nguondon,ghichugiaohang,makm,dauvantayyeucau,datrutonkho,daghinhanthanhtoan)
            VALUES(@id,LOCALTIMESTAMP,@kind,@method,@subtotal,@discount,@vat,@total,0,0,@phone,@kh,@address,'Nội bộ',@shipping,'CHUA_THANH_TOAN',0,@cod,@emp,@name,'WEB',@note,@voucher,@fp,FALSE,FALSE)
            """,ct,("id",id),("kind",request.PaymentMethod=="COD"?"COD":"WEB_CHO_THANH_TOAN"),("method",request.PaymentMethod),("subtotal",p.Subtotal),("discount",p.Discount),("vat",p.VatAmount),("total",p.Total),("phone",request.Phone.Trim()),("kh",customer),("address",request.Address.Trim()),("shipping",NativeStoreCodes.ShippingFor(request.PaymentMethod)),("cod",request.PaymentMethod=="COD"?p.Total:0m),("emp",mode.WebOrderEmployeeId),("name",request.Recipient.Trim()),("note",request.Note?.Trim()??""),("voucher",p.Voucher),("fp",fingerprint.Value));
        decimal cumulative=0,allocated=0;
        for(var i=0;i<lines.Count;i++){var l=lines[i];cumulative+=l.Total;var next=Math.Round(p.Total*cumulative/p.Subtotal,0,MidpointRounding.AwayFromZero);var actual=next-allocated;allocated=next;
            await Execute(c,tx,"INSERT INTO public.chitiethoadon(sohd,masku,soluong,dongia,thanhtien,giatrithuctra) VALUES(@id,@sku,@qty,@price,@total,@actual)",ct,("id",id),("sku",l.Sku),("qty",l.Quantity),("price",l.UnitPrice),("total",l.Total),("actual",actual));}
        await ReserveInvoiceStock(c,tx,id,ct);
        await tx.CommitAsync(ct);return new(id,request.PaymentMethod=="COD"?"XAC_NHAN":"MOI",p.Total,request.PaymentMethod);
    }
    public async Task<IReadOnlyList<OrderSummary>> ListAsync(string customer,CancellationToken ct,string? q=null,string? status=null,int page=1){
        await using var c=Connection();await c.OpenAsync(ct);await using var cmd=Sql(c,null,$"SELECT h.sohd,h.ngaylap,{StatusSql},COALESCE(h.trangthaigiaohang,''),h.trangthaithanhtoan,h.phuongthucthanhtoan,h.khachphaitra,COALESCE(h.tennguoinhan,'') FROM public.hoadon h WHERE h.makh=@kh AND strpos(lower(h.sohd),lower(@q))>0 AND (@status='' OR ({StatusSql})=@status) ORDER BY h.ngaylap DESC,h.sohd DESC LIMIT 100 OFFSET @skip",("kh",customer),("q",q??""),("status",status??""),("skip",(page-1)*100));
        var items=new List<OrderSummary>();await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))items.Add(new(r.GetString(0),r.GetDateTime(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetDecimal(6),r.GetString(7)));return items;
    }
    public async Task<OrderDetails> DetailAsync(string id,string customer,CancellationToken ct){
        await using var c=Connection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);OrderDetails order;
        await using(var cmd=Sql(c,tx,$"SELECT h.sohd,h.ngaylap,{StatusSql},COALESCE(h.trangthaigiaohang,''),h.trangthaithanhtoan,h.phuongthucthanhtoan,h.khachphaitra,COALESCE(h.tennguoinhan,''),COALESCE(h.sodienthoai_kh,''),COALESCE(h.diachigiao,''),h.ghichugiaohang,h.tongcong,h.chietkhau,h.makm,h.lydohuy,h.ngayhuy,h.thuevat,COALESCE(h.phiship,0) FROM public.hoadon h WHERE h.sohd=@id AND h.makh=@kh",("id",id),("kh",customer)))await using(var r=await cmd.ExecuteReaderAsync(ct)){
            if(!await r.ReadAsync(ct))throw new CustomerRequestException(404,"Không tìm thấy đơn hàng.");
            order=new(r.GetString(0),r.GetDateTime(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetDecimal(6),r.GetString(7),r.GetString(8),r.GetString(9),r.IsDBNull(10)?null:r.GetString(10),[],r.GetDecimal(11),r.GetDecimal(12),r.IsDBNull(13)?null:r.GetString(13)){CancellationReason=r.IsDBNull(14)?null:r.GetString(14),CancelledAt=r.IsDBNull(15)?null:r.GetDateTime(15),VatAmount=r.GetDecimal(16),ShippingFee=r.GetDecimal(17)};
        }
        var lines=new List<OrderLine>();await using(var cmd=Sql(c,tx,"SELECT l.masku,COALESCE(l.tensanphamlucmua,p.tensanpham),COALESCE(l.kichcolucmua,p.kichco),COALESCE(l.mausaclucmua,p.mausac),l.soluong,l.dongia,l.thanhtien,l.hinhanhlucmua,COALESCE(l.masplucmua,p.masp) FROM public.chitiethoadon l JOIN public.sanpham p USING(masku) WHERE l.sohd=@id ORDER BY l.masku",("id",id)))await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))lines.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt32(4),r.GetDecimal(5),r.GetDecimal(6),r.IsDBNull(7)?null:r.GetString(7)){ProductId=r.GetString(8)});
        order=await WithRefundDetails(c,tx,order with{Items=lines,SupportsHistory=true,SupportsReturns=await IsWebOrder(c,tx,id,ct)},customer,ct);order=await WithUpdateCapabilities(c,tx,order,customer,ct);await tx.CommitAsync(ct);return order;
    }
    public async Task CancelAsync(string customer,string id,CancellationToken ct,CancelOrderRequest? input=null){
        await using var c=Connection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);var s=await ReadUpdateState(c,tx,customer,id,true,ct)??throw new CustomerRequestException(404,"Không tìm thấy đơn hàng.");
        if(s.Kind=="WEB_HUY"||s.Shipping is "Đã hủy" or "Đã huỷ"){await tx.CommitAsync(ct);return;}
        if(!s.CanCancel)throw new CustomerRequestException(409,"Chỉ được hủy đơn chưa thanh toán ở bước chờ hoặc đã xác nhận. Đơn đã có giao dịch thanh toán cần cửa hàng xử lý.");
        var reason=input?.Reason?.Trim();if(string.IsNullOrWhiteSpace(reason)||reason.Length>500)throw new CustomerRequestException(400,"Vui lòng chọn lý do hủy tối đa 500 ký tự.");
        await Execute(c,tx,"UPDATE public.hoadon SET loaihoadon=CASE WHEN phuongthucthanhtoan='COD' THEN 'COD' ELSE 'GIAO_HANG' END,trangthaigiaohang='Đã hủy',tiencod=0,lydohuy=@reason,ngayhuy=CURRENT_TIMESTAMP WHERE sohd=@id",ct,("reason",reason),("id",id));await tx.CommitAsync(ct);
    }

}
