using FashionStore.Backend.Middleware;
using Npgsql;

namespace FashionStore.Backend.Services;

public sealed partial class SupabaseCustomerFeaturesService
{
    private const string VerifiedPurchase = """
        FROM public.hoadon h JOIN public.chitiethoadon d ON d.sohd=h.sohd
        WHERE h.makh=@kh AND h.nguondon='WEB' AND d.masplucmua=@sp
          AND h.trangthaigiaohang='Hàng giao thành công' AND h.trangthaithanhtoan='DA_THANH_TOAN'
          AND h.loaihoadon<>'WEB_HUY'
        """;

    public Task<List<Dictionary<string,object?>>> Reviews(string id,int page,CancellationToken ct) => Query("""
        SELECT d.sosao AS "SoSao",d.noidungdanhgia AS "NoiDung",d.ngaydanhgia AS "NgayDanhGia",
            COALESCE(NULLIF(btrim(k.hoten),''),'Khách hàng') AS "TenHienThi"
        FROM public.chitiethoadon d JOIN public.hoadon h USING(sohd) JOIN public.khachhang k USING(makh)
        WHERE d.masplucmua=@sp AND d.trangthaidanhgia='HIEN_THI'
          AND EXISTS(SELECT 1 FROM public.sanpham p JOIN public.loaisanpham l USING(maloai)
                     WHERE p.masp=d.masplucmua AND p.trangthai AND l.trangthai)
        ORDER BY d.ngaydanhgia DESC,d.sohd,d.masku LIMIT 10 OFFSET @skip
        """,ct,("sp",id),("skip",(page-1)*10));

    public Task<List<Dictionary<string,object?>>> MyReviews(string kh,string product,int page,CancellationToken ct) => Query("""
        SELECT d.sohd||':'||d.masku AS "MaDanhGia",d.sohd AS "SoHD",d.sosao AS "SoSao",d.noidungdanhgia AS "NoiDung",
            d.ngaydanhgia AS "NgayDanhGia",d.trangthaidanhgia AS "TrangThai",
            COALESCE(NULLIF(btrim(k.hoten),''),'Khách hàng') AS "TenHienThi"
        FROM public.chitiethoadon d JOIN public.hoadon h USING(sohd) JOIN public.khachhang k USING(makh)
        WHERE h.makh=@kh AND d.masplucmua=@sp AND d.sosao IS NOT NULL
        ORDER BY d.ngaydanhgia DESC,d.sohd,d.masku LIMIT 10 OFFSET @skip
        """,ct,("kh",kh),("sp",product),("skip",(page-1)*10));

    public Task<List<Dictionary<string,object?>>> ReviewOrders(string kh,string product,CancellationToken ct) => Query(
        "SELECT DISTINCT h.sohd AS \"SoHD\",h.ngaylap AT TIME ZONE 'UTC' AS \"NgayLap\" " + VerifiedPurchase + " " + """
        AND NOT EXISTS(SELECT 1 FROM public.chitiethoadon r
                       WHERE r.sohd=h.sohd AND r.masplucmua=@sp AND r.sosao IS NOT NULL)
        ORDER BY "NgayLap" DESC,"SoHD" DESC
        """,ct,("kh",kh),("sp",product));

    public async Task Review(string kh,ReviewInput input,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(input.ProductId)||string.IsNullOrWhiteSpace(input.OrderId)||input.Stars is <1 or >5||
            string.IsNullOrWhiteSpace(input.Body)||input.Body.Length>2000)
            throw new CustomerRequestException(400,"Vui lòng chọn sản phẩm, đơn hàng, số sao từ 1 đến 5 và nhập nhận xét tối đa 2.000 ký tự.");
        await using var c=Connection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await using(var invoice=Command(c,tx,"SELECT sohd FROM public.hoadon WHERE sohd=@hd AND makh=@kh AND nguondon='WEB' FOR UPDATE",("hd",input.OrderId),("kh",kh)))
            if(await invoice.ExecuteScalarAsync(ct)==null)throw NotEligible();
        string? sku;
        await using(var eligible=Command(c,tx,"SELECT d.masku "+VerifiedPurchase+" AND h.sohd=@hd ORDER BY d.masku LIMIT 1",
            ("kh",kh),("sp",input.ProductId),("hd",input.OrderId)))sku=await eligible.ExecuteScalarAsync(ct) as string;
        if(sku==null)throw NotEligible();
        await using(var duplicate=Command(c,tx,"SELECT EXISTS(SELECT 1 FROM public.chitiethoadon WHERE sohd=@hd AND masplucmua=@sp AND sosao IS NOT NULL)",("hd",input.OrderId),("sp",input.ProductId)))
            if(Convert.ToBoolean(await duplicate.ExecuteScalarAsync(ct)))throw new CustomerRequestException(409,"Mỗi sản phẩm trong một đơn chỉ được đánh giá một lần.");
        try
        {
            await using var save=Command(c,tx,"""
                UPDATE public.chitiethoadon SET sosao=@stars,noidungdanhgia=@body,ngaydanhgia=CURRENT_TIMESTAMP,trangthaidanhgia='HIEN_THI'
                WHERE sohd=@hd AND masku=@sku AND sosao IS NULL
                """,("stars",input.Stars),("body",input.Body.Trim()),("hd",input.OrderId),("sku",sku));
            if(await save.ExecuteNonQueryAsync(ct)!=1)throw NotEligible();
            await tx.CommitAsync(ct);
        }
        catch(PostgresException e) when(e.SqlState is "23505" or "23514") {throw NotEligible();}
    }

    private static CustomerRequestException NotEligible() => new(409,"Chỉ được đánh giá sản phẩm thuộc đơn đã nhận hàng, đã thanh toán của bạn và chưa đánh giá.");
}
