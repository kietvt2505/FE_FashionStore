using System.Data;
using System.Security.Cryptography;
using System.Text;
using FashionStore.Backend.Data;
using FashionStore.Backend.Middleware;
using Npgsql;

namespace FashionStore.Backend.Services;

public sealed class SupabaseCustomerFeaturesService(DatabaseMode mode)
{
    internal NpgsqlConnection Connection() => new(mode.SupabaseConnection);
    internal static NpgsqlCommand Command(NpgsqlConnection c, NpgsqlTransaction? tx, string sql, params (string,object?)[] args)
    {
        var cmd = new NpgsqlCommand(sql,c,tx) { CommandTimeout=15 };
        foreach (var (name,value) in args) cmd.Parameters.AddWithValue(name,value ?? DBNull.Value);
        return cmd;
    }
    private async Task<List<Dictionary<string,object?>>> Query(string sql,CancellationToken ct,params (string,object?)[] args)
    {
        await using var c=Connection(); await c.OpenAsync(ct);
        await using var cmd=Command(c,null,sql,args); await using var r=await cmd.ExecuteReaderAsync(ct);
        var items=new List<Dictionary<string,object?>>();
        while(await r.ReadAsync(ct)) {
            var row=new Dictionary<string,object?>();
            for(var i=0;i<r.FieldCount;i++) row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i);
            items.Add(row);
        }
        return items;
    }
    private static async Task LockCustomer(NpgsqlConnection c,NpgsqlTransaction tx,string customer,CancellationToken ct)
    {
        await using var cmd=Command(c,tx,"SELECT makh FROM public.khachhang WHERE makh=@kh AND trangthai=true FOR UPDATE",("kh",customer));
        if(await cmd.ExecuteScalarAsync(ct) is null) throw new CustomerRequestException(401,"Vui lòng đăng nhập lại.");
    }
    public Task<List<Dictionary<string,object?>>> Addresses(string kh,CancellationToken ct) => Query("""
        SELECT id AS "Id",recipient AS "Recipient",phone AS "Phone",address AS "Address"
        FROM webstore.address WHERE makh=@kh ORDER BY id DESC
        """,ct,("kh",kh));
    public async Task SaveAddress(string kh,AddressInput input,CancellationToken ct)
    {
        await using var c=Connection(); await c.OpenAsync(ct);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await LockCustomer(c,tx,kh,ct);
        await using(var count=Command(c,tx,"SELECT count(*) FROM webstore.address WHERE makh=@kh",("kh",kh)))
            if(Convert.ToInt32(await count.ExecuteScalarAsync(ct))>=20) throw new CustomerRequestException(409,"Tối đa 20 địa chỉ.");
        await using var cmd=Command(c,tx,"INSERT INTO webstore.address(makh,recipient,phone,address) VALUES(@kh,@name,@phone,@address)",
            ("kh",kh),("name",input.Recipient.Trim()),("phone",input.Phone.Trim()),("address",input.Address.Trim()));
        await cmd.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task RemoveAddress(string kh,long id,CancellationToken ct)
    {
        await using var c=Connection(); await c.OpenAsync(ct);
        await using var cmd=Command(c,null,"DELETE FROM webstore.address WHERE makh=@kh AND id=@id",("kh",kh),("id",id));
        await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task UpdateAddress(string kh,long id,AddressInput input,CancellationToken ct)
    {
        await using var c=Connection(); await c.OpenAsync(ct);
        await using var cmd=Command(c,null,"UPDATE webstore.address SET recipient=@name,phone=@phone,address=@address WHERE makh=@kh AND id=@id",
            ("name",input.Recipient.Trim()),("phone",input.Phone.Trim()),("address",input.Address.Trim()),("kh",kh),("id",id));
        if(await cmd.ExecuteNonQueryAsync(ct)!=1) throw new CustomerRequestException(404,"Không tìm thấy địa chỉ của bạn.");
    }
    public Task<List<Dictionary<string,object?>>> Wishlist(string kh,CancellationToken ct) => Query("""
        SELECT w.masp AS "MaSP",COALESCE(min(p.tensanpham),'Sản phẩm không còn bán') AS "TenSanPham",
            COALESCE(bool_or(p.trangthai AND l.trangthai),false) AS "TrangThai",
            min(p.hinhanh) AS "ImageUrl"
        FROM webstore.wishlist w LEFT JOIN public.sanpham p ON p.masp=w.masp
        LEFT JOIN public.loaisanpham l ON l.maloai=p.maloai
        WHERE w.makh=@kh GROUP BY w.masp,w.createdat ORDER BY w.createdat DESC,w.masp
        """,ct,("kh",kh));
    public async Task SaveWishlist(string kh,string id,bool remove,CancellationToken ct)
    {
        await using var c=Connection(); await c.OpenAsync(ct);
        if(!remove) {
            await using var product=Command(c,null,"SELECT 1 FROM public.sanpham p JOIN public.loaisanpham l USING(maloai) WHERE p.masp=@id AND p.trangthai AND l.trangthai LIMIT 1",("id",id));
            if(await product.ExecuteScalarAsync(ct) is null) throw new CustomerRequestException(404,"Sản phẩm không khả dụng.");
        }
        await using var cmd=Command(c,null,remove ? "DELETE FROM webstore.wishlist WHERE makh=@kh AND masp=@id" :
            "INSERT INTO webstore.wishlist(makh,masp) VALUES(@kh,@id) ON CONFLICT(makh,masp) DO NOTHING",("kh",kh),("id",id));
        await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task<List<Dictionary<string,object?>>> Promotions(CancellationToken ct)
    {
        bool checkout;
        await using(var c=Connection()) {
            await c.OpenAsync(ct);
            checkout=await SupabaseCheckoutPricing.VouchersInstalled(c,null,ct);
        }
        if(checkout)return await Query("""
            SELECT makm AS "MaKM",tenchuongtrinh AS "TenChuongTrinh",kind AS "LoaiKM",value AS "GiaTriGiam",
                minimum AS "GiaTriDonHangToiThieu",(ends_on::timestamp+INTERVAL '1 day'-INTERVAL '1 microsecond') AS "NgayKetThuc",
                NULL::integer AS "SoLuotToiDa",0 AS "SoLuotDaDung",product_promotion AS "ProductPromotion",
                (NOT product_promotion AND minimum>=0 AND ((kind='PHAN_TRAM' AND value<100) OR kind='SO_TIEN')) AS "CheckoutSupported"
            FROM webstore.checkout_voucher WHERE active AND value>0 AND kind IS NOT NULL
                AND (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh')::date BETWEEN starts_on AND ends_on
            ORDER BY ends_on,makm
            """,ct);
        return await Query("""
        SELECT makm AS "MaKM",tenchuongtrinh AS "TenChuongTrinh",
            CASE WHEN lower(btrim(loaikm)) IN ('phần trăm','phan_tram','phan tram','%') THEN 'PHAN_TRAM' ELSE 'SO_TIEN' END AS "LoaiKM",
            giatrigiam AS "GiaTriGiam",COALESCE(giatridonhangtoithieu,0) AS "GiaTriDonHangToiThieu",
            (ngayketthuc::timestamp+INTERVAL '1 day'-INTERVAL '1 microsecond') AS "NgayKetThuc",NULL::integer AS "SoLuotToiDa",0 AS "SoLuotDaDung",
            (@checkout AND ((lower(btrim(loaikm)) IN ('phần trăm','phan_tram','phan tram','%') AND giatrigiam<100)
              OR lower(btrim(loaikm)) IN ('số tiền','so_tien','so tien','vnd'))) AS "CheckoutSupported"
        FROM public.khuyenmai WHERE trangthai AND CURRENT_DATE BETWEEN ngaybatdau AND ngayketthuc AND giatrigiam>0 ORDER BY ngayketthuc,makm
        """,ct,("checkout",checkout));
    }
    public async Task<List<Dictionary<string,object?>>> Points(string kh,int page,CancellationToken ct)
    {
        await using(var c=Connection()) {
            await c.OpenAsync(ct);
            await using var check=Command(c,null,"SELECT to_regclass('webstore.payment_confirmation') IS NOT NULL");
            if(!Convert.ToBoolean(await check.ExecuteScalarAsync(ct))) return [];
        }
        return await Query("""
            SELECT p.sohd AS "SoHD",'TICH' AS "LoaiGiaoDich",floor(p.amount/10000)::integer AS "SoDiemThayDoi",
                p.confirmedat AT TIME ZONE 'UTC' AS "ThoiDiem",'Điểm được ghi nhận khi đối soát chuyển khoản website' AS "GhiChu"
            FROM webstore.payment_confirmation p JOIN webstore.order_snapshot s ON s.sohd=p.sohd
            WHERE s.makh=@kh ORDER BY p.confirmedat DESC,p.sohd LIMIT 20 OFFSET @skip
            """,ct,("kh",kh),("skip",(page-1)*20));
    }
    public Task<List<Dictionary<string,object?>>> History(string kh,string id,CancellationToken ct) => Query("""
        SELECT 'DON_HANG' AS "LoaiTrangThai",'MOI' AS "TrangThaiMoi",
            s.ngaylap AT TIME ZONE 'UTC' AS "ThoiDiem",'Đơn được tạo từ website' AS "GhiChu"
        FROM webstore.order_snapshot s WHERE s.makh=@kh AND s.sohd=@id
        UNION ALL
        SELECT 'GIAO_HANG','DA_GIAO',h.ngaygiaothanhcong AT TIME ZONE 'UTC','Cửa hàng ghi nhận giao hàng thành công'
        FROM public.hoadon h JOIN webstore.order_snapshot s ON s.sohd=h.sohd AND s.makh=h.makh
        WHERE s.makh=@kh AND s.sohd=@id AND h.trangthaigiaohang='Hàng giao thành công' AND h.ngaygiaothanhcong IS NOT NULL
        ORDER BY "ThoiDiem"
        """,ct,("kh",kh),("id",id));
    public Task<List<Dictionary<string,object?>>> Reviews(string id,int page,CancellationToken ct) => Query("""
        SELECT r.stars AS "SoSao",r.body AS "NoiDung",r.createdat AT TIME ZONE 'UTC' AS "NgayDanhGia",'Khách hàng' AS "TenHienThi"
        FROM webstore.product_review r WHERE r.masp=@id AND r.status='HIEN_THI'
        AND EXISTS(SELECT 1 FROM public.sanpham p JOIN public.loaisanpham c USING(maloai) WHERE p.masp=r.masp AND p.trangthai AND c.trangthai)
        ORDER BY r.createdat DESC,r.id DESC LIMIT 10 OFFSET @skip
        """,ct,("id",id),("skip",(page-1)*10));
    private const string ReviewPurchases = """
        FROM public.hoadon h JOIN webstore.order_snapshot s ON s.sohd=h.sohd AND s.makh=h.makh
        JOIN webstore.order_line_snapshot l ON l.sohd=h.sohd JOIN public.sanpham p ON p.masku=l.masku
        WHERE h.makh=@kh AND p.masp=@sp AND h.trangthaigiaohang='Hàng giao thành công'
            AND h.trangthaithanhtoan='DA_THANH_TOAN' AND h.loaihoadon IS DISTINCT FROM 'WEB_HUY'
        """;
    public Task<List<Dictionary<string,object?>>> ReviewOrders(string kh,string product,CancellationToken ct) => Query(
        "SELECT DISTINCT s.sohd AS \"SoHD\",s.ngaylap AT TIME ZONE 'UTC' AS \"NgayLap\" " + ReviewPurchases + " " + """
        AND NOT EXISTS(SELECT 1 FROM webstore.product_review r WHERE r.makh=@kh AND r.masp=@sp AND r.sohd=h.sohd)
        ORDER BY "NgayLap" DESC,"SoHD" DESC
        """,ct,("kh",kh),("sp",product));
    public async Task Review(string kh,ReviewInput input,CancellationToken ct)
    {
        await using var c=Connection(); await c.OpenAsync(ct);
        await using var tx=await c.BeginTransactionAsync(ct);
        // A WinForms status update and a review submission must not pass each other mid-check.
        await using(var invoice=Command(c,tx,"SELECT sohd FROM public.hoadon WHERE sohd=@hd AND makh=@kh FOR UPDATE",("hd",input.OrderId),("kh",kh)))
            if(await invoice.ExecuteScalarAsync(ct) is null)
                throw new CustomerRequestException(409,"Chỉ được đánh giá sản phẩm thuộc đơn đã giao hàng thành công và thanh toán của bạn.");
        await using var cmd=Command(c,tx,"""
            INSERT INTO webstore.product_review(makh,masp,sohd,stars,body)
            SELECT @kh,@sp,@hd,@stars,@body WHERE EXISTS(SELECT 1
            """ + " " + ReviewPurchases + " AND h.sohd=@hd) ON CONFLICT(makh,masp,sohd) DO NOTHING",
            ("kh",kh),("sp",input.ProductId),("hd",input.OrderId),("stars",input.Stars),("body",input.Body.Trim()));
        if(await cmd.ExecuteNonQueryAsync(ct)!=1)
            throw new CustomerRequestException(409,"Chỉ được đánh giá sản phẩm thuộc đơn đã giao hàng thành công và thanh toán của bạn. Mỗi sản phẩm trong một đơn chỉ được đánh giá một lần.");
        await tx.CommitAsync(ct);
    }
    public Task<List<Dictionary<string,object?>>> Returns(string kh,int page,CancellationToken ct) => Query("""
        SELECT sophieudt AS "SoPhieuDT",sohd AS "SoHD",createdat AT TIME ZONE 'UTC' AS "NgayLap",kind AS "HinhThucXuLy",
            status AS "TrangThai",reason AS "LyDo",rejectionreason AS "LyDoTuChoi"
        FROM webstore.return_request WHERE makh=@kh ORDER BY createdat DESC,sophieudt LIMIT 20 OFFSET @skip
        """,ct,("kh",kh),("skip",(page-1)*20));
    public async Task CreateReturn(string kh,ReturnInput input,CancellationToken ct)
    {
        if(input.RequestId==Guid.Empty) throw new CustomerRequestException(400,"Vui lòng tải lại biểu mẫu đổi trả.");
        var id="WR"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kh+":"+input.RequestId.ToString("D"))))[..28];
        await using var c=Connection(); await c.OpenAsync(ct);
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await using(var invoice=Command(c,tx,"""
            SELECT h.sohd FROM public.hoadon h JOIN webstore.order_snapshot s ON s.sohd=h.sohd AND s.makh=h.makh
            WHERE h.sohd=@hd AND h.makh=@kh AND h.trangthaigiaohang='Hàng giao thành công' AND h.trangthaithanhtoan='DA_THANH_TOAN'
            AND h.ngaygiaothanhcong BETWEEN LOCALTIMESTAMP-INTERVAL '15 days' AND LOCALTIMESTAMP FOR UPDATE OF h
            """,("hd",input.OrderId),("kh",kh)))
            if(await invoice.ExecuteScalarAsync(ct) is null) throw new CustomerRequestException(409,"Đơn chưa giao thành công, quá 15 ngày hoặc không thuộc tài khoản.");
        var duplicate=false;
        await using(var existing=Command(c,tx,"SELECT sohd,masku,quantity,kind,newsku,reason FROM webstore.return_request WHERE sophieudt=@id AND makh=@kh",("id",id),("kh",kh)))
        await using(var r=await existing.ExecuteReaderAsync(ct)) {
            if(await r.ReadAsync(ct)) {
                if(r.GetString(0)!=input.OrderId || r.GetString(1)!=input.Sku || r.GetInt32(2)!=input.Quantity ||
                    r.GetString(3)!=input.Kind || (r.IsDBNull(4)?null:r.GetString(4))!=(input.Kind=="DOI"?input.NewSku:null) || r.GetString(5)!=input.Reason.Trim())
                    throw new CustomerRequestException(409,"Mã yêu cầu đã được dùng với nội dung khác. Vui lòng tải lại biểu mẫu.");
                duplicate=true;
            }
        }
        if(duplicate) { await tx.CommitAsync(ct); return; }
        await using(var available=Command(c,tx,"""
            SELECT l.quantity-COALESCE((SELECT sum(r.quantity) FROM webstore.return_request r WHERE r.sohd=l.sohd AND r.masku=l.masku AND r.status IN ('CHO_DUYET','DA_DUYET')),0)
              -COALESCE((SELECT sum(d.soluong) FROM public.chitietdoitra d JOIN public.phieudoitra p USING(sophieudt) WHERE p.sohd=l.sohd AND d.masku=l.masku AND p.trangthai='HOAN_TAT'),0)
            FROM webstore.order_line_snapshot l WHERE l.sohd=@hd AND l.masku=@sku
            """,("hd",input.OrderId),("sku",input.Sku))) {
            var remaining=await available.ExecuteScalarAsync(ct);
            if(remaining is null || input.Quantity>Convert.ToInt64(remaining)) throw new CustomerRequestException(409,"Số lượng đổi trả vượt số lượng còn hợp lệ.");
        }
        if(input.Kind=="DOI") {
            await using var replacement=Command(c,tx,"SELECT p.masku FROM public.sanpham p JOIN public.loaisanpham l USING(maloai) WHERE p.masku=@sku AND p.trangthai AND l.trangthai AND p.soluongtonkho>=@qty FOR SHARE OF p,l",("sku",input.NewSku ?? ""),("qty",input.Quantity));
            if(await replacement.ExecuteScalarAsync(ct) is null) throw new CustomerRequestException(409,"SKU đổi mới không hợp lệ hoặc không đủ tồn kho.");
        }
        await using var cmd=Command(c,tx,"""
            INSERT INTO webstore.return_request(sophieudt,sohd,makh,masku,quantity,kind,newsku,reason)
            VALUES(@id,@hd,@kh,@sku,@qty,@kind,@newsku,@reason)
            """,("id",id),("hd",input.OrderId),("kh",kh),("sku",input.Sku),("qty",input.Quantity),("kind",input.Kind),("newsku",input.Kind=="DOI"?input.NewSku:null),("reason",input.Reason.Trim()));
        await cmd.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
    }
}
