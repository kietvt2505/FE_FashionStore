using System.Data;
using System.Security.Cryptography;
using System.Text;
using FashionStore.Backend.Data;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Middleware;
using Npgsql;

namespace FashionStore.Backend.Services;

// COD is completed by Windows. Prepaid invoices enter its delivery queue only after verified settlement.
public sealed class SupabaseOrderService(DatabaseMode mode,PaymentGatewaySettings? paymentSettings=null)
{
    private NpgsqlConnection Connection()
    {
        if (!mode.OrdersEnabled) throw new CustomerRequestException(503, "Đặt hàng chưa được bật.");
        return new NpgsqlConnection(mode.SupabaseConnection);
    }
    private static NpgsqlCommand Sql(NpgsqlConnection c, NpgsqlTransaction? tx, string sql,
        params (string Name, object Value)[] args)
    {
        var cmd = new NpgsqlCommand(sql, c, tx) { CommandTimeout = 15 };
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
    private static async Task Execute(NpgsqlConnection c, NpgsqlTransaction tx, string sql,
        CancellationToken ct, params (string Name, object Value)[] args)
    {
        await using var cmd = Sql(c, tx, sql, args); await cmd.ExecuteNonQueryAsync(ct);
    }
    private static async Task<List<OrderLine>> ReadCart(NpgsqlConnection c, NpgsqlTransaction tx, string customer, bool locking, CancellationToken ct)
    {
        var lines = new List<OrderLine>();
        await using var priceCheck=Sql(c,tx,"SELECT to_regclass('webstore.sku_price') IS NOT NULL");
        var nativePricing=Convert.ToBoolean(await priceCheck.ExecuteScalarAsync(ct));
        await using var cmd = Sql(c, tx, $"""
            SELECT p.masku,p.tensanpham,p.kichco,p.mausac,l.soluong,{(nativePricing?"price.sale_price":"p.dongia")},p.soluongtonkho,p.trangthai AND cat.trangthai
            FROM webstore.cart g JOIN webstore.cart_line l USING(magiohang)
            JOIN public.sanpham p USING(masku) JOIN public.loaisanpham cat USING(maloai)
            {(nativePricing?"JOIN webstore.sku_price price ON price.masku=p.masku":"")}
            WHERE g.makh=@customer ORDER BY p.masku
            """ + (locking ? " FOR UPDATE OF g,l,p FOR SHARE OF cat" : ""), ("customer", customer));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var quantity = r.GetInt32(4); var price = r.GetDecimal(5);
            if (!r.GetBoolean(7) || quantity < 1 || quantity > r.GetInt32(6) || price <= 0)
                throw new CustomerRequestException(409, "Giá hoặc tồn kho đã thay đổi. Vui lòng kiểm tra giỏ hàng.");
            lines.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), quantity, price, quantity * price));
        }
        if (lines.Count == 0) throw new CustomerRequestException(400, "Giỏ hàng đang trống.");
        return lines;
    }
    public async Task<CheckoutQuote> QuoteAsync(string customer, string? voucher, CancellationToken ct)
    {
        await using var c = Connection(); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var lines = await ReadCart(c, tx, customer, false, ct);
        var installed = await SupabaseCheckoutPricing.IsInstalled(c, tx, ct);
        var price = await SupabaseCheckoutPricing.Calculate(c, tx, installed, customer, voucher, lines.Sum(l => l.Total), ct);
        await tx.CommitAsync(ct);
        return new(price.Subtotal, price.Discount, price.Total)
        { MembershipEnabled=true,MemberRank=price.Rank,MemberRate=price.Rate,MemberPoints=price.Points,
            MemberDiscount=price.MemberDiscount,Voucher=price.Voucher,VoucherDiscount=price.VoucherDiscount };
    }
    public async Task<PlaceOrderResponse> PlaceAsync(string customer, CheckoutRequest request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || (request.PaymentMethod != "COD" && request.PaymentMethod != "CHUYEN_KHOAN" && !PaymentGatewaySettings.Online(request.PaymentMethod)))
            throw new CustomerRequestException(400, "Phương thức thanh toán không hợp lệ.");
        if (string.IsNullOrWhiteSpace(mode.WebOrderEmployeeId))
            throw new CustomerRequestException(503, "Cửa hàng chưa cấu hình nhân viên tiếp nhận đơn.");
        // Retain lookup of orders created before short numbers were introduced.
        var legacyId = "WEB" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(customer + ":" + request.RequestId.ToString("D"))))[..27];
        await using var c = Connection(); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using (var lockCustomer = Sql(c, tx, "SELECT makh FROM public.khachhang WHERE makh=@customer AND trangthai=true FOR UPDATE", ("customer", customer)))
            if (await lockCustomer.ExecuteScalarAsync(ct) is null) throw new CustomerRequestException(401, "Vui lòng đăng nhập lại.");
        bool shortNumbersInstalled;
        await using(var installed=Sql(c,tx,"""
            SELECT to_regclass('webstore.web_order_number') IS NOT NULL AND EXISTS(
                SELECT 1 FROM information_schema.columns WHERE table_schema='webstore'
                  AND table_name='order_snapshot' AND column_name='request_id')
            """))shortNumbersInstalled=Convert.ToBoolean(await installed.ExecuteScalarAsync(ct));
        await using (var exists = Sql(c, tx, "SELECT sohd FROM webstore.order_snapshot WHERE makh=@customer AND (sohd=@legacy"+
            (shortNumbersInstalled?" OR request_id=@request":"")+")", ("legacy", legacyId), ("customer", customer),("request",request.RequestId)))
            if (await exists.ExecuteScalarAsync(ct) is string previousId)
            {
                await tx.CommitAsync(ct);
                var previous = await DetailAsync(previousId, customer, ct);
                return new(previous.OrderId, previous.Status, previous.Total, previous.PaymentMethod);
            }
        if(!shortNumbersInstalled)
            throw new CustomerRequestException(503,"Mã đơn hàng mới chưa được cập nhật tại cửa hàng. Vui lòng thử lại sau.");
        var online=PaymentGatewaySettings.Online(request.PaymentMethod);
        var transfer = request.PaymentMethod == "CHUYEN_KHOAN" || online;
        if (online && paymentSettings?.Ready(request.PaymentMethod)!=true)
            throw new CustomerRequestException(409,"Phương thức thanh toán trực tuyến chưa được cấu hình tại cửa hàng.");
        if(online) {
            if(request.PaymentMethod=="PAYOS") {
                await using var payos=Sql(c,tx,"SELECT to_regclass('webstore.payos_payment_number') IS NOT NULL");
                if(!Convert.ToBoolean(await payos.ExecuteScalarAsync(ct)))throw new CustomerRequestException(503,"Cửa hàng chưa cập nhật thanh toán QR payOS.");
            }
            await using var paymentCheck=Sql(c,tx,"SELECT to_regprocedure('webstore.settle_gateway_payment(character varying,character varying,character varying,numeric,character varying)') IS NOT NULL");
            if(!Convert.ToBoolean(await paymentCheck.ExecuteScalarAsync(ct)))throw new CustomerRequestException(503,"Cấu trúc thanh toán trực tuyến chưa được cập nhật tại cửa hàng.");
        }
        if (request.PaymentMethod == "CHUYEN_KHOAN" && !mode.BankTransferEnabled)
            throw new CustomerRequestException(409, "Chuyển khoản chưa được bật tại cửa hàng.");
        await using (var employee = Sql(c, tx, "SELECT manv FROM public.nhanvien WHERE manv=@employee AND trangthai=true FOR SHARE", ("employee", mode.WebOrderEmployeeId)))
            if (await employee.ExecuteScalarAsync(ct) is null)
                throw new CustomerRequestException(503, "Nhân viên tiếp nhận đơn không tồn tại hoặc đã ngừng hoạt động.");
        var lines = await ReadCart(c, tx, customer, true, ct);
        var pricingInstalled = await SupabaseCheckoutPricing.IsInstalled(c, tx, ct);
        var price = await SupabaseCheckoutPricing.Calculate(c, tx, pricingInstalled, customer, request.Voucher, lines.Sum(l => l.Total), ct);
        var total = price.Total;
        if(online)PaymentGatewaySettings.Amount(total);
        if (total <= 0 || price.Subtotal > 9999999999999999m) throw new CustomerRequestException(400, "Giá trị đơn hàng không hợp lệ.");
        string id="";
        bool dailyNumbers;
        await using(var daily=Sql(c,tx,"SELECT to_regprocedure('webstore.allocate_order_number(date)') IS NOT NULL"))
            dailyNumbers=Convert.ToBoolean(await daily.ExecuteScalarAsync(ct));
        if(dailyNumbers) {
            try {await using var next=Sql(c,tx,"SELECT webstore.allocate_order_number()");id=(string)(await next.ExecuteScalarAsync(ct))!;}
            catch(PostgresException e) when(e.SqlState=="P0001") {throw new CustomerRequestException(503,"Cửa hàng đã dùng hết mã đơn trong ngày. Vui lòng liên hệ cửa hàng.");}
        }
        // Sequence numbers are shared by all website instances. Also skip numbers entered manually in POS.
        for(var attempt=0;!dailyNumbers && attempt<10;attempt++)
        {
            await using var next=Sql(c,tx,"SELECT nextval('webstore.web_order_number')");
            var candidate=WebOrderNumber.Format(Convert.ToInt64(await next.ExecuteScalarAsync(ct)));
            await using var used=Sql(c,tx,"SELECT EXISTS(SELECT 1 FROM public.hoadon WHERE sohd=@id UNION ALL SELECT 1 FROM webstore.order_snapshot WHERE sohd=@id)",("id",candidate));
            if(!Convert.ToBoolean(await used.ExecuteScalarAsync(ct))){id=candidate;break;}
        }
        if(id.Length==0)throw new CustomerRequestException(503,"Chưa thể cấp mã đơn hàng. Vui lòng thử lại sau.");
        await Execute(c, tx, """
            INSERT INTO public.hoadon(sohd,ngaylap,loaihoadon,phuongthucthanhtoan,tongcong,chietkhau,thuevat,
                khachphaitra,tienkhachdua,tienthua,sodienthoai_kh,makh,tennguoinhan,diachigiao,donvivanchuyen,
                trangthaigiaohang,trangthaithanhtoan,phiship,tiencod,manv,nguondon,ghichugiaohang,makm)
            VALUES(@id,CURRENT_TIMESTAMP AT TIME ZONE 'UTC',@type,@method,@subtotal,@discount,0,@total,0,0,@phone,@customer,@name,@address,
                'Nội bộ',@shipping,'CHUA_THANH_TOAN',0,@cod,@employee,'WEB',@note,@voucher);
            INSERT INTO webstore.order_snapshot(sohd,makh,ngaylap,recipient,phone,address,note,total,paymentmethod,request_id)
            VALUES(@id,@customer,CURRENT_TIMESTAMP AT TIME ZONE 'UTC',@name,@phone,@address,@note,@total,@method,@request);
            """, ct, ("id", id), ("total", total), ("subtotal", price.Subtotal), ("discount", price.Discount), ("voucher", (object?)price.Voucher ?? DBNull.Value), ("customer", customer), ("phone", request.Phone.Trim()),
            ("name", request.Recipient.Trim()), ("address", request.Address.Trim()), ("note", request.Note?.Trim() ?? ""), ("employee", mode.WebOrderEmployeeId),
            ("type", transfer ? "WEB_CHO_THANH_TOAN" : "COD"), ("method", request.PaymentMethod),
            ("shipping", transfer ? "Chờ xác nhận thanh toán" : "Chờ xác nhận"), ("cod", transfer ? 0m : total),("request",request.RequestId));
        if (price.MembershipInstalled)
            await Execute(c,tx,"""
                INSERT INTO webstore.order_pricing(sohd,subtotal,discount,voucher,member_rank,member_rate,member_points,member_discount)
                VALUES(@id,@subtotal,@discount,@voucher,@rank,@rate,@points,@memberDiscount)
                """,ct,("id",id),("subtotal",price.Subtotal),("discount",price.Discount),("voucher",(object?)price.Voucher??DBNull.Value),
                ("rank",price.Rank),("rate",price.Rate),("points",price.Points),("memberDiscount",price.MemberDiscount));
        else if (pricingInstalled)
            await Execute(c, tx, "INSERT INTO webstore.order_pricing(sohd,subtotal,discount,voucher) VALUES(@id,@subtotal,@discount,@voucher)", ct,
                ("id", id), ("subtotal", price.Subtotal), ("discount", price.Discount), ("voucher", (object?)price.Voucher ?? DBNull.Value));
        foreach (var line in lines)
            await Execute(c, tx, """
                INSERT INTO public.chitiethoadon(sohd,masku,soluong,dongia,thanhtien) VALUES(@id,@sku,@qty,@price,@total);
                INSERT INTO webstore.order_line_snapshot(sohd,masku,name,size,color,quantity,price)
                VALUES(@id,@sku,@name,@size,@color,@qty,@price);
                """, ct, ("id", id), ("sku", line.Sku), ("qty", line.Quantity), ("price", line.UnitPrice),
                ("total", line.Total), ("name", line.Name), ("size", line.Size), ("color", line.Color));
        await Execute(c, tx, "DELETE FROM webstore.cart_line WHERE magiohang IN (SELECT magiohang FROM webstore.cart WHERE makh=@customer)", ct, ("customer", customer));
        await tx.CommitAsync(ct);
        return new(id, "MOI", total, request.PaymentMethod);
    }
    private const string StatusSql = """
        CASE WHEN h.sohd IS NULL THEN 'CAN_HO_TRO'
             WHEN h.trangthaigiaohang='Đã nhận tiền - cần cửa hàng xử lý' THEN 'CAN_HO_TRO'
             WHEN h.loaihoadon='WEB_HUY' THEN 'HUY'
             WHEN h.loaihoadon='WEB_CHO_THANH_TOAN' THEN 'MOI'
             WHEN h.trangthaigiaohang IN ('MOI','CHO_XAC_NHAN','CHUA_GIAO','Chờ xác nhận','Đang chờ xác nhận') THEN 'MOI'
             WHEN h.trangthaigiaohang='Hàng giao thành công' THEN 'HOAN_TAT'
             WHEN h.trangthaigiaohang IN ('Shipper đã lấy hàng','Hàng đang được vận chuyển') THEN 'DANG_XU_LY'
             ELSE 'XAC_NHAN' END
        """;
    public async Task<IReadOnlyList<OrderSummary>> ListAsync(string customer, CancellationToken ct, string? q = null, string? status = null, int page = 1)
    {
        await using var c = Connection(); await c.OpenAsync(ct);
        await using var cmd = Sql(c, null, $"""
            SELECT s.sohd,s.ngaylap AT TIME ZONE 'UTC',{StatusSql},COALESCE(h.trangthaigiaohang,'Cần liên hệ cửa hàng'),
                COALESCE(h.trangthaithanhtoan,'CHUA_XAC_MINH'),COALESCE(h.khachphaitra,s.total),s.recipient,s.paymentmethod
            FROM webstore.order_snapshot s LEFT JOIN public.hoadon h ON h.sohd=s.sohd AND h.makh=s.makh
            WHERE s.makh=@customer AND strpos(lower(s.sohd),lower(@q))>0 AND (@status='' OR ({StatusSql})=@status)
            ORDER BY s.ngaylap DESC,s.sohd DESC LIMIT 100 OFFSET @skip
            """, ("customer", customer), ("q", q ?? ""), ("status", status ?? ""), ("skip", (page - 1) * 100));
        var items = new List<OrderSummary>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) items.Add(new(r.GetString(0), r.GetDateTime(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(7), r.GetDecimal(5), r.GetString(6)));
        return items;
    }
    public async Task<OrderDetails> DetailAsync(string id, string customer, CancellationToken ct)
    {
        await using var c = Connection(); await c.OpenAsync(ct);
        // Repeatable read keeps header and lines consistent, while ownership is always server-side.
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        OrderDetails order;
        await using (var cmd = Sql(c, tx, $"""
            SELECT s.sohd,s.ngaylap AT TIME ZONE 'UTC',{StatusSql},COALESCE(h.trangthaigiaohang,'Cần liên hệ cửa hàng'),
                COALESCE(h.trangthaithanhtoan,'CHUA_XAC_MINH'),COALESCE(h.khachphaitra,s.total),s.recipient,s.phone,s.address,s.note,s.total,s.paymentmethod
            FROM webstore.order_snapshot s LEFT JOIN public.hoadon h ON h.sohd=s.sohd AND h.makh=s.makh
            WHERE s.sohd=@id AND s.makh=@customer
            """, ("id", id), ("customer", customer)))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) throw new CustomerRequestException(404, "Không tìm thấy đơn hàng.");
            order = new(r.GetString(0), r.GetDateTime(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(11), r.GetDecimal(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9), [], r.GetDecimal(10));
        }
        var lines = new List<OrderLine>();
        await using (var cmd = Sql(c, tx, """
            SELECT l.masku,l.name,l.size,l.color,l.quantity,l.price,
                CASE WHEN lower(btrim(p.mausac))=lower(btrim(l.color)) THEN p.hinhanh END,p.masp
            FROM webstore.order_line_snapshot l
            LEFT JOIN public.sanpham p ON p.masku=l.masku
            WHERE l.sohd=@id ORDER BY l.masku
            """, ("id", id)))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) lines.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetDecimal(5), r.GetInt32(4) * r.GetDecimal(5), r.IsDBNull(6) ? null : r.GetString(6)) { ProductId = r.IsDBNull(7) ? null : r.GetString(7) });
        await using var features = Sql(c, tx, "SELECT to_regclass('webstore.return_request') IS NOT NULL");
        var enabled = Convert.ToBoolean(await features.ExecuteScalarAsync(ct));
        if (await SupabaseCheckoutPricing.IsInstalled(c, tx, ct))
        {
            await using var pricing = Sql(c, tx, "SELECT subtotal,discount,voucher FROM webstore.order_pricing WHERE sohd=@id", ("id", id));
            await using var r = await pricing.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct)) order = order with { Subtotal = r.GetDecimal(0), Discount = r.GetDecimal(1), Voucher = r.IsDBNull(2) ? null : r.GetString(2) };
        }
        if(await SupabaseCheckoutPricing.MembershipInstalled(c,tx,ct))
        {
            await using var pricing=Sql(c,tx,"SELECT member_rank,member_rate,member_points,member_discount FROM webstore.order_pricing WHERE sohd=@id",("id",id));
            await using var r=await pricing.ExecuteReaderAsync(ct);
            if(await r.ReadAsync(ct))order=order with {MemberRank=r.IsDBNull(0)?null:r.GetString(0),MemberRate=r.GetInt16(1),MemberPoints=r.IsDBNull(2)?null:r.GetInt32(2),MemberDiscount=r.GetDecimal(3)};
        }
        await using(var installed=Sql(c,tx,"SELECT to_regprocedure('webstore.cancel_customer_order(character varying,character varying)') IS NOT NULL"))
        if(Convert.ToBoolean(await installed.ExecuteScalarAsync(ct))) {
            await using var cancellation=Sql(c,tx,"SELECT webstore.can_cancel_order(@customer,@id),(SELECT cancelledat FROM webstore.order_cancellation WHERE sohd=@id AND makh=@customer),(SELECT reason FROM webstore.order_cancellation WHERE sohd=@id AND makh=@customer)",("customer",customer),("id",id));
            await using var reader=await cancellation.ExecuteReaderAsync(ct);
            if(await reader.ReadAsync(ct))order=order with {CanCancel=reader.GetBoolean(0) && OrderCancellationPolicy.AllowsShippingStage(order.ShippingStatus),CancelledAt=reader.IsDBNull(1)?null:reader.GetDateTime(1),CancellationReason=reader.IsDBNull(2)?null:reader.GetString(2)};
        }
        await using(var installed=Sql(c,tx,"SELECT to_regclass('webstore.order_refund') IS NOT NULL"))
        if(Convert.ToBoolean(await installed.ExecuteScalarAsync(ct))) {
            await using var refund=Sql(c,tx,"SELECT status,amount,account_holder,CASE WHEN account_number IS NOT NULL THEN '•••• '||right(account_number,4) END,bank_name,refundedat FROM webstore.order_refund WHERE sohd=@id AND makh=@customer",("id",id),("customer",customer));
            await using var reader=await refund.ExecuteReaderAsync(ct);
            if(await reader.ReadAsync(ct))order=order with {Refund=new(reader.GetString(0),reader.GetDecimal(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.IsDBNull(5)?null:reader.GetDateTime(5))};
        }
        await tx.CommitAsync(ct);
        return order with { Items = lines, SupportsHistory = enabled, SupportsReturns = enabled,
            VoucherDiscount=order.Voucher==null?0:Math.Max(0,order.Discount-order.MemberDiscount) };
    }
    public async Task CancelAsync(string customer,string id,CancellationToken ct,CancelOrderRequest? input=null)
    {
        await using var c=Connection();await c.OpenAsync(ct);
        await using(var owner=Sql(c,null,"SELECT EXISTS(SELECT 1 FROM webstore.order_snapshot WHERE sohd=@id AND makh=@customer)",("id",id),("customer",customer)))
            if(!Convert.ToBoolean(await owner.ExecuteScalarAsync(ct)))throw new CustomerRequestException(404,"Không tìm thấy đơn hàng.");
        await using(var installed=Sql(c,null,"SELECT to_regprocedure('webstore.cancel_customer_order(character varying,character varying)') IS NOT NULL"))
            if(!Convert.ToBoolean(await installed.ExecuteScalarAsync(ct)))throw new CustomerRequestException(503,"Cửa hàng chưa bật tính năng hủy đơn trực tuyến. Vui lòng liên hệ để được hỗ trợ.");
        bool refundInstalled;
        await using(var installed=Sql(c,null,"SELECT to_regprocedure('webstore.cancel_customer_order(character varying,character varying,text,character varying,character varying,character varying)') IS NOT NULL"))
            refundInstalled=Convert.ToBoolean(await installed.ExecuteScalarAsync(ct));
        await using var tx=await c.BeginTransactionAsync(ct);
        try {
            // Hold the invoice lock through cancellation. WinForms cannot advance shipping between this check and the SQL function.
            // Keep duplicate cancellation requests idempotent, including after a late payment was marked for review.
            await using(var check=Sql(c,tx,"""
                SELECT h.trangthaigiaohang,h.loaihoadon,s.paymentmethod
                FROM public.hoadon h JOIN webstore.order_snapshot s ON s.sohd=h.sohd AND s.makh=h.makh
                WHERE s.sohd=@id AND s.makh=@customer FOR UPDATE OF h
                """,("id",id),("customer",customer)))
            await using(var reader=await check.ExecuteReaderAsync(ct)) {
                if(!await reader.ReadAsync(ct) || (reader.GetString(1)!="WEB_HUY" &&
                    !OrderCancellationPolicy.AllowsShippingStage(reader.IsDBNull(0)?null:reader.GetString(0))))
                    throw new CustomerRequestException(409,OrderCancellationPolicy.RejectionMessage);
                if(reader.GetString(2)!="COD" && !refundInstalled)
                    throw new CustomerRequestException(503,"Cửa hàng chưa cập nhật biểu mẫu hủy và hoàn tiền. Vui lòng thử lại sau.");
            }
            await using var cmd=Sql(c,tx,refundInstalled?"SELECT webstore.cancel_customer_order(@customer,@id,@reason::text,@holder::varchar,@account::varchar,@bank::varchar)":"SELECT webstore.cancel_customer_order(@customer,@id)",
                ("customer",customer),("id",id),("reason",(object?)input?.Reason??DBNull.Value),("holder",(object?)input?.AccountHolder??DBNull.Value),
                ("account",(object?)input?.AccountNumber??DBNull.Value),("bank",(object?)input?.BankName??DBNull.Value));
            await cmd.ExecuteScalarAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch(PostgresException e) when(e.SqlState=="P0002") {throw new CustomerRequestException(404,"Không tìm thấy đơn hàng.");}
        catch(PostgresException e) when(e.SqlState=="P0001") {throw new CustomerRequestException(409,OrderCancellationPolicy.RejectionMessage);}
        catch(PostgresException e) when(e.SqlState=="22023") {throw new CustomerRequestException(400,"Vui lòng nhập lý do hủy từ 5–500 ký tự; đơn đã thanh toán cần đủ tên chủ tài khoản, số tài khoản và ngân hàng nhận hoàn tiền.");}
    }

    public async Task SaveRefundAccountAsync(string customer,string id,CancelOrderRequest input,CancellationToken ct)
    {
        await using var c=Connection();await c.OpenAsync(ct);
        await using(var installed=Sql(c,null,"SELECT to_regprocedure('webstore.submit_refund_account(character varying,character varying,character varying,character varying,character varying)') IS NOT NULL"))
            if(!Convert.ToBoolean(await installed.ExecuteScalarAsync(ct)))throw new CustomerRequestException(503,"Cửa hàng chưa bật yêu cầu hoàn tiền.");
        try {
            await using var cmd=Sql(c,null,"SELECT webstore.submit_refund_account(@customer,@id,@holder::varchar,@account::varchar,@bank::varchar)",
                ("customer",customer),("id",id),("holder",(object?)input.AccountHolder??DBNull.Value),("account",(object?)input.AccountNumber??DBNull.Value),("bank",(object?)input.BankName??DBNull.Value));
            await cmd.ExecuteScalarAsync(ct);
        }
        catch(PostgresException e) when(e.SqlState=="P0002") {throw new CustomerRequestException(404,"Không tìm thấy đơn hàng.");}
        catch(PostgresException e) when(e.SqlState=="P0001") {throw new CustomerRequestException(409,"Thông tin hoàn tiền đã được gửi hoặc đơn không còn chờ thông tin. Vui lòng tải lại trang.");}
        catch(PostgresException e) when(e.SqlState=="22023") {throw new CustomerRequestException(400,"Vui lòng nhập đủ tên chủ tài khoản, số tài khoản gồm 6–34 chữ số và ngân hàng.");}
    }
}
