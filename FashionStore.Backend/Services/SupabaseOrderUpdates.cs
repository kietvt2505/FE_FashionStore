using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Middleware;
using Npgsql;

namespace FashionStore.Backend.Services;

public sealed partial class SupabaseOrderService
{
    private sealed record UpdateState(string Kind,string Shipping,string Payment,string Method,string InvoiceMethod,
        decimal Received,bool Delivered,string Source,string InvoiceVersion,string SnapshotVersion,bool HasAttempt,decimal Total)
    {
        public bool CanDelivery => Source=="WEB" && !Delivered &&
            (Kind is "COD" or "WEB_COD" or "WEB_CHO_THANH_TOAN" or "GIAO_HANG" or "WEB_DA_THANH_TOAN") &&
            OrderCancellationPolicy.AllowsShippingStage(Shipping) && (Payment is "CHUA_THANH_TOAN" or "DA_THANH_TOAN" or "THAT_BAI");
        public bool CanPayment => CanDelivery && (Kind is "COD" or "WEB_COD" or "WEB_CHO_THANH_TOAN") &&
            Payment=="CHUA_THANH_TOAN" && Received==0 && !HasAttempt && Method==InvoiceMethod;
        public string Version => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(InvoiceVersion+":"+SnapshotVersion+":"+HasAttempt)));
    }

    private static async Task<UpdateState?> ReadUpdateState(NpgsqlConnection c,NpgsqlTransaction tx,string customer,string id,bool locking,CancellationToken ct)
    {
        bool attemptsInstalled,nativeQrInstalled;
        await using(var installed=Sql(c,tx,"SELECT to_regclass('public.yeucauthanhtoantructuyen') IS NOT NULL"))
            attemptsInstalled=Convert.ToBoolean(await installed.ExecuteScalarAsync(ct));
        await using(var nativeQr=Sql(c,tx,"SELECT to_regclass('public.giaodichthanhtoanqr') IS NOT NULL"))
            nativeQrInstalled=Convert.ToBoolean(await nativeQr.ExecuteScalarAsync(ct));
        await using var command=Sql(c,tx,$"""
            SELECT h.loaihoadon,COALESCE(h.trangthaigiaohang,''),COALESCE(h.trangthaithanhtoan,''),s.paymentmethod,
                COALESCE(h.phuongthucthanhtoan,''),COALESCE(h.tienkhachdua,0),h.ngaygiaothanhcong IS NOT NULL,
                COALESCE(h.nguondon,''),h.xmin::text,s.xmin::text,
                ({(attemptsInstalled?"EXISTS(SELECT 1 FROM public.yeucauthanhtoantructuyen a WHERE a.sohd=h.sohd)":"false")}
                 OR {(nativeQrInstalled?"EXISTS(SELECT 1 FROM public.giaodichthanhtoanqr q WHERE q.sohd=h.sohd)":"false")}),h.khachphaitra
            FROM public.hoadon h JOIN public.thongtindonhang s ON s.sohd=h.sohd AND s.makh=h.makh
            WHERE s.sohd=@id AND s.makh=@customer
            """+(locking?" FOR UPDATE OF h,s":""),("id",id),("customer",customer));
        await using var r=await command.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),
            r.GetDecimal(5),r.GetBoolean(6),r.GetString(7),r.GetString(8),r.GetString(9),r.GetBoolean(10),r.GetDecimal(11)):null;
    }

    private async Task<OrderDetails> WithUpdateCapabilities(NpgsqlConnection c,NpgsqlTransaction tx,OrderDetails order,string customer,CancellationToken ct)
    {
        var state=await ReadUpdateState(c,tx,customer,order.OrderId,false,ct);
        return order with {CanUpdateDelivery=state?.CanDelivery==true,CanUpdatePayment=state?.CanPayment==true,
            SupportsBankTransfer=mode.BankTransferEnabled,EditVersion=state?.Version,
            PaymentUpdateNotice=state?.HasAttempt==true?"Đơn đã tạo giao dịch thanh toán online. Vui lòng liên hệ cửa hàng để đổi phương thức.":
                state?.Payment=="DA_THANH_TOAN"?"Đơn đã thanh toán nên không thể đổi phương thức thanh toán.":null};
    }

    public async Task UpdateInformationAsync(string customer,string id,UpdateOrderRequest input,CancellationToken ct)
    {
        if(input.Section is not ("delivery" or "payment") || string.IsNullOrEmpty(input.Version))
            throw new CustomerRequestException(400,"Yêu cầu cập nhật đơn không hợp lệ.");
        if(input.Section=="delivery" && (string.IsNullOrWhiteSpace(input.Recipient)||input.Recipient.Trim().Length is <2 or >150 ||
            string.IsNullOrWhiteSpace(input.Phone)||!Regex.IsMatch(input.Phone,@"^\+?[0-9]{9,15}$") ||
            string.IsNullOrWhiteSpace(input.Address)||input.Address.Trim().Length is <10 or >300))
            throw new CustomerRequestException(400,"Vui lòng nhập đủ tên người nhận, số điện thoại và địa chỉ hợp lệ.");
        await using var c=Connection();await c.OpenAsync(ct);
        await using var tx=await c.BeginTransactionAsync(ct);
        // Acquire the same invoice lock as payment creation/settlement, then read a fresh statement snapshot.
        // A payment attempt committed while waiting for this lock must be visible to eligibility checks.
        await using(var invoiceLock=Sql(c,tx,"""
            SELECT h.sohd FROM public.hoadon h JOIN public.thongtindonhang s ON s.sohd=h.sohd AND s.makh=h.makh
            WHERE s.sohd=@id AND s.makh=@customer FOR UPDATE OF h
            """,("id",id),("customer",customer)))
            if(await invoiceLock.ExecuteScalarAsync(ct)==null)throw new CustomerRequestException(404,"Không tìm thấy đơn hàng.");
        var state=await ReadUpdateState(c,tx,customer,id,true,ct)
            ?? throw new CustomerRequestException(404,"Không tìm thấy đơn hàng.");
        if(state.Version!=input.Version)throw new CustomerRequestException(409,"Thông tin đơn đã thay đổi. Vui lòng tải lại trang trước khi cập nhật.");
        if(input.Section=="delivery")
        {
            if(!state.CanDelivery)throw new CustomerRequestException(409,"Chỉ được cập nhật địa chỉ trước khi đơn được chuẩn bị hoặc giao hàng.");
            await Execute(c,tx,"""
                UPDATE public.hoadon SET tennguoinhan=@name,sodienthoai_kh=@phone,diachigiao=@address WHERE sohd=@id;
                UPDATE public.thongtindonhang SET recipient=@name,phone=@phone,address=@address WHERE sohd=@id AND makh=@customer;
                """,ct,("id",id),("customer",customer),("name",input.Recipient!.Trim()),("phone",input.Phone!.Trim()),("address",input.Address!.Trim()));
        }
        else
        {
            if(!state.CanPayment)throw new CustomerRequestException(409,"Chỉ được đổi phương thức cho đơn chưa thanh toán, chưa tạo giao dịch online và chưa chuẩn bị giao hàng.");
            var method=input.PaymentMethod;
            if(method!="COD" && method!="CHUYEN_KHOAN" && (method==null || !PaymentGatewaySettings.Online(method)))
                throw new CustomerRequestException(400,"Phương thức thanh toán không hợp lệ.");
            if(method==state.Method){await tx.CommitAsync(ct);return;}
            if(method=="CHUYEN_KHOAN" && !mode.BankTransferEnabled)
                throw new CustomerRequestException(409,"Chuyển khoản chưa được bật tại cửa hàng.");
            if(PaymentGatewaySettings.Online(method))
            {
                if(paymentSettings?.Ready(method)!=true)throw new CustomerRequestException(409,"Phương thức thanh toán online chưa được cấu hình tại cửa hàng.");
                PaymentGatewaySettings.Amount(state.Total);
                await using var installed=Sql(c,tx,"SELECT to_regprocedure('public.settle_gateway_payment(character varying,character varying,character varying,numeric,character varying)') IS NOT NULL"+
                    (method=="PAYOS"?" AND to_regclass('public.payos_payment_number') IS NOT NULL":""));
                if(!Convert.ToBoolean(await installed.ExecuteScalarAsync(ct)))throw new CustomerRequestException(503,"Cửa hàng chưa cập nhật thanh toán online.");
            }
            // Payment changes return to the matching pending queue. Amounts, discounts and invoice lines stay fixed.
            await Execute(c,tx,"""
                UPDATE public.hoadon SET phuongthucthanhtoan=@method,loaihoadon=@kind,trangthaigiaohang=@shipping,tiencod=@cod
                    WHERE sohd=@id;
                UPDATE public.thongtindonhang SET paymentmethod=@method WHERE sohd=@id AND makh=@customer;
                """,ct,("id",id),("customer",customer),("method",method),("kind",method=="COD"?"COD":"WEB_CHO_THANH_TOAN"),
                ("shipping",method=="COD"?"Chờ xác nhận":"Chờ xác nhận thanh toán"),("cod",method=="COD"?state.Total:0m));
        }
        await tx.CommitAsync(ct);
    }
}
