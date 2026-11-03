using FashionStore.Backend.Middleware;
using Npgsql;

namespace FashionStore.Backend.Services;

public sealed partial class SupabaseOrderService
{
    private static async Task EnsureInventoryContract(NpgsqlConnection c,NpgsqlTransaction tx,CancellationToken ct)
    {
        await using var cmd=Sql(c,tx,"SELECT count(*)=2 FROM information_schema.columns WHERE table_schema='public' AND table_name='hoadon' AND column_name IN ('datrutonkho','daghinhanthanhtoan') AND data_type='boolean'");
        if(await cmd.ExecuteScalarAsync(ct) is not true)
            throw new CustomerRequestException(503,"Cấu trúc giữ tồn kho chưa đồng bộ với form mới. Cửa hàng cần áp dụng bản đồng bộ WinForms trước khi nhận đơn.");
    }
    private static async Task<bool> IsWebOrder(NpgsqlConnection c,NpgsqlTransaction tx,string id,CancellationToken ct)
    {
        await using var cmd=Sql(c,tx,"SELECT nguondon='WEB' FROM public.hoadon WHERE sohd=@id",("id",id));
        return await cmd.ExecuteScalarAsync(ct) is true;
    }
    // Same invoice flags and stock timing as WinForms 6d11b10; shipment updates see TRUE
    // and do not deduct again. An unknown historical flag needs staff reconciliation.
    private static async Task ReserveInvoiceStock(NpgsqlConnection c,NpgsqlTransaction tx,string id,CancellationToken ct)
    {
        await using(var flag=Sql(c,tx,"SELECT datrutonkho FROM public.hoadon WHERE sohd=@id FOR UPDATE",("id",id)))
        {
            var value=await flag.ExecuteScalarAsync(ct);
            if(value is true)return;
            if(value is not false)throw new CustomerRequestException(409,"Đơn cũ cần cửa hàng đối chiếu tồn kho trước khi đổi phương thức thanh toán.");
        }
        await using(var locks=Sql(c,tx,"SELECT s.masku FROM public.sanpham s JOIN public.chitiethoadon l USING(masku) WHERE l.sohd=@id ORDER BY s.masku FOR UPDATE OF s",("id",id)))
        await using(var r=await locks.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct)) { }
        await using(var check=Sql(c,tx,"SELECT count(*)>0 AND bool_and(s.soluongtonkho>=l.soluong AND l.soluong>0) FROM public.chitiethoadon l JOIN public.sanpham s USING(masku) WHERE l.sohd=@id",("id",id)))
            if(await check.ExecuteScalarAsync(ct) is not true)throw new CustomerRequestException(409,"Tồn kho không đủ để giữ hàng cho đơn.");
        await Execute(c,tx,"""
            UPDATE public.sanpham s SET soluongtonkho=s.soluongtonkho-l.soluong,soluong=s.soluong-l.soluong
            FROM public.chitiethoadon l WHERE l.sohd=@id AND l.masku=s.masku;
            UPDATE public.hoadon SET datrutonkho=TRUE WHERE sohd=@id;
            """,ct,("id",id));
    }
}
