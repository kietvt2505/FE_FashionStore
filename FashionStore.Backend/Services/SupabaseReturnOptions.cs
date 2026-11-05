using System.Data;
using System.Text.RegularExpressions;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Middleware;
using Npgsql;

namespace FashionStore.Backend.Services;

public sealed record CustomerReturnVariant(string Sku,string Color,string Size,decimal UnitPrice,int Stock,bool Available);
public sealed record CustomerReturnLine(string Sku,string ProductId,string Name,string Color,string Size,
    int PurchasedQuantity,int RemainingQuantity,int ProcessedQuantity,decimal PaidTotal,decimal ProcessedValue,List<CustomerReturnVariant> Variants);
public sealed record CustomerReturnOptions(string OrderId,string Recipient,string Phone,string Address,DateTime DeliveredAt,
    DateTime Deadline,List<CustomerReturnLine> Items,int VatRate=8);

public sealed partial class SupabaseCustomerFeaturesService
{
    public async Task<CustomerReturnOptions> ReturnOptions(string customer,string order,CancellationToken ct)
    {
        await using var c=Connection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        string recipient,phone,address,shipping,payment,source;DateTime? delivered;decimal paid;bool inWindow;
        await using(var cmd=Command(c,tx,"""
            SELECT COALESCE(h.tennguoinhan,k.hoten,''),COALESCE(h.sodienthoai_kh,k.sodienthoai,''),COALESCE(h.diachigiao,''),
                COALESCE(h.trangthaigiaohang,''),h.trangthaithanhtoan,h.nguondon,h.ngaygiaothanhcong,
                GREATEST(0,h.khachphaitra-COALESCE(h.phiship,0)),
                COALESCE(h.ngaygiaothanhcong BETWEEN LOCALTIMESTAMP-INTERVAL '15 days' AND LOCALTIMESTAMP,FALSE)
            FROM public.hoadon h LEFT JOIN public.khachhang k USING(makh) WHERE h.sohd=@id AND h.makh=@kh
            """,("id",order),("kh",customer)))
        {
            await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CustomerRequestException(404,"Không tìm thấy đơn hàng.");
            recipient=r.GetString(0);phone=r.GetString(1);address=r.GetString(2);shipping=r.GetString(3);payment=r.GetString(4);source=r.GetString(5);
            delivered=r.IsDBNull(6)?null:r.GetDateTime(6);paid=r.GetDecimal(7);inWindow=r.GetBoolean(8);
        }
        if(source!="WEB" || shipping!="Hàng giao thành công" || payment!="DA_THANH_TOAN" || delivered is null || !inWindow)
            throw new CustomerRequestException(409,"Chỉ đổi/trả đơn đã nhận và thanh toán trong thời hạn 15 ngày.");
        var raw=new List<(string Sku,string Product,string Name,string Color,string Size,int Qty,decimal Price,decimal? Paid)>();
        await using(var cmd=Command(c,tx,"""
            SELECT d.masku,s.masp,s.tensanpham,s.mausac,s.kichco,d.soluong,d.dongia,d.giatrithuctra
            FROM public.chitiethoadon d JOIN public.sanpham s USING(masku) WHERE d.sohd=@id ORDER BY d.masku
            """,("id",order)))
        {await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))raw.Add((r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt32(5),r.GetDecimal(6),r.IsDBNull(7)?null:r.GetDecimal(7)));}
        var result=new List<CustomerReturnLine>();decimal cumulative=0,allocated=0;var subtotal=raw.Sum(x=>x.Qty*x.Price);
        foreach(var line in raw)
        {
            cumulative+=line.Qty*line.Price;var next=subtotal==0?0:Math.Round(paid*cumulative/subtotal,0,MidpointRounding.AwayFromZero);
            var paidLine=line.Paid??next-allocated;allocated=next;
            int reserved,processed;decimal processedValue;
            await using(var cmd=Command(c,tx,"""
                SELECT COALESCE(SUM(d.soluong) FILTER(WHERE p.trangthai<>'TU_CHOI'),0),
                    COALESCE(SUM(d.soluong) FILTER(WHERE p.trangthai='HOAN_TAT'),0),
                    COALESCE(SUM(d.thanhtiennhanlai) FILTER(WHERE p.trangthai='HOAN_TAT'),0)
                FROM public.chitietdoitra d JOIN public.phieudoitra p USING(sophieudt) WHERE p.sohd=@id AND d.masku=@sku
                """,("id",order),("sku",line.Sku)))
            {await using var r=await cmd.ExecuteReaderAsync(ct);await r.ReadAsync(ct);reserved=Convert.ToInt32(r.GetInt64(0));processed=Convert.ToInt32(r.GetInt64(1));processedValue=r.GetDecimal(2);}
            var variants=new List<CustomerReturnVariant>();
            await using(var cmd=Command(c,tx,"""
                SELECT p.masku,p.mausac,p.kichco,LEAST(p.dongia,COALESCE(k.giakhuyenmai,p.dongia)),p.soluongtonkho,p.trangthai AND l.trangthai
                FROM public.sanpham p JOIN public.loaisanpham l USING(maloai)
                LEFT JOIN public.sanphamkhuyenmai_hientai k USING(masku)
                WHERE p.masp=@product AND ((p.trangthai AND l.trangthai) OR p.masku=@sku) ORDER BY p.mausac,p.kichco,p.masku
                """,("product",line.Product),("sku",line.Sku)))
            {await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))variants.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetDecimal(3),r.GetInt32(4),r.GetBoolean(5)));}
            result.Add(new(line.Sku,line.Product,line.Name,line.Color,line.Size,line.Qty,Math.Max(0,line.Qty-reserved),processed,paidLine,processedValue,variants));
        }
        await tx.CommitAsync(ct);return new(order,recipient,phone,address,delivered.Value,delivered.Value.AddDays(15),result);
    }

    internal static void ValidateReturnBank(string? holder,string? number,string? bank)
    {
        if(string.IsNullOrWhiteSpace(holder)||holder.Trim().Length>150||!Regex.IsMatch(number??"","^[0-9]{6,34}$")||string.IsNullOrWhiteSpace(bank)||bank.Trim().Length>100)
            throw new CustomerRequestException(400,"Nhập đủ tên chủ tài khoản, số tài khoản 6–34 chữ số và ngân hàng nhận tiền.");
    }

    public async Task SaveReturnRefundAccount(string customer,string id,CancelOrderRequest input,CancellationToken ct)
    {
        ValidateReturnBank(input.AccountHolder,input.AccountNumber,input.BankName);
        await using var c=Connection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await using(var cmd=Command(c,tx,"""
            SELECT p.trangthai,p.sotienhoan,p.ngayhoantien FROM public.phieudoitra p JOIN public.hoadon h USING(sohd)
            WHERE p.sophieudt=@id AND h.makh=@kh AND h.nguondon='WEB' AND p.nguondon='WEB' FOR UPDATE OF p
            """,("id",id),("kh",customer)))
        {
            await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CustomerRequestException(404,"Không tìm thấy phiếu đổi/trả.");
            if(r.GetString(0)!="HOAN_TAT"||r.GetDecimal(1)<=0||!r.IsDBNull(2))throw new CustomerRequestException(409,"Chỉ bổ sung tài khoản cho phiếu đã duyệt, có tiền cần hoàn và chưa hoàn tiền.");
        }
        await SupabaseOrderService.Execute(c,tx,"""
            UPDATE public.phieudoitra SET chutaikhoanhoantien=@holder,sotaikhoanhoantien=@number,tennganhanghoantien=@bank WHERE sophieudt=@id
            """,ct,("holder",input.AccountHolder!.Trim()),("number",input.AccountNumber),("bank",input.BankName!.Trim()),("id",id));
        await tx.CommitAsync(ct);
    }
}
