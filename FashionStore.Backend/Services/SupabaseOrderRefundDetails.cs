using System.Text.RegularExpressions;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Middleware;
using Npgsql;

namespace FashionStore.Backend.Services;

public sealed partial class SupabaseOrderService
{
    private static async Task<OrderDetails> WithRefundDetails(NpgsqlConnection c,NpgsqlTransaction tx,OrderDetails order,string customer,CancellationToken ct)
    {
        await using var cmd=Sql(c,tx,"""
            SELECT p.sotienhoan,p.chutaikhoanhoantien,
                CASE WHEN p.sotaikhoanhoantien IS NOT NULL THEN '•••• '||right(p.sotaikhoanhoantien,4) END,
                p.tennganhanghoantien,p.ngayhoantien,
                CASE WHEN p.ngayhoantien IS NOT NULL AND p.trangthai='HOAN_TAT' THEN 'DA_HOAN_TIEN'
                     WHEN p.sotaikhoanhoantien IS NULL THEN 'CHO_THONG_TIN' ELSE 'CHO_HOAN_TIEN' END
            FROM public.phieudoitra p JOIN public.hoadon h USING(sohd)
            WHERE h.sohd=@id AND h.makh=@kh AND p.nguondon='WEB' AND p.sotienhoan>0 AND p.trangthai<>'TU_CHOI'
            ORDER BY p.ngaylap DESC,p.sophieudt DESC LIMIT 1
            """,("id",order.OrderId),("kh",customer));
        await using var r=await cmd.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))return order;
        return order with{Refund=new(r.GetString(5),r.GetDecimal(0),r.IsDBNull(1)?null:r.GetString(1),
            r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetDateTime(4))};
    }

    public async Task SaveRefundAccountAsync(string customer,string id,CancelOrderRequest input,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(input.AccountHolder)||input.AccountHolder.Trim().Length>150||
           string.IsNullOrWhiteSpace(input.BankName)||input.BankName.Trim().Length>100||
           !Regex.IsMatch(input.AccountNumber??"","^[0-9]{6,34}$"))
            throw new CustomerRequestException(400,"Vui lòng nhập đủ chủ tài khoản, số tài khoản 6–34 chữ số và ngân hàng nhận tiền.");
        await using var c=Connection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        string requestId;
        await using(var find=Sql(c,tx,"""
            SELECT p.sophieudt FROM public.phieudoitra p JOIN public.hoadon h USING(sohd)
            WHERE h.sohd=@id AND h.makh=@kh AND h.nguondon='WEB' AND p.nguondon='WEB'
              AND (p.trangthai IN ('CHO_DUYET','CHO_TIEP_NHAN','DA_DUYET') OR (p.trangthai='HOAN_TAT' AND p.sotienhoan>0)) AND p.ngayhoantien IS NULL
            ORDER BY p.ngaylap DESC,p.sophieudt DESC LIMIT 1 FOR UPDATE OF p
            """,("id",id),("kh",customer)))
            requestId=await find.ExecuteScalarAsync(ct) as string??throw new CustomerRequestException(409,"Chưa có phiếu đổi trả đang chờ xử lý hoặc phiếu đã hoàn tất. Vui lòng liên hệ cửa hàng.");
        await Execute(c,tx,"""
            UPDATE public.phieudoitra SET chutaikhoanhoantien=@holder,sotaikhoanhoantien=@number,tennganhanghoantien=@bank
            WHERE sophieudt=@request
            """,ct,("holder",input.AccountHolder.Trim()),("number",input.AccountNumber),("bank",input.BankName.Trim()),("request",requestId));
        await tx.CommitAsync(ct);
    }
}
