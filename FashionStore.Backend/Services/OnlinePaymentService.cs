using System.Data;
using FashionStore.Backend.Data;
using FashionStore.Backend.Middleware;
using Npgsql;

namespace FashionStore.Backend.Services;

public sealed class OnlinePaymentService(DatabaseMode mode,PaymentGatewaySettings settings,PaymentGatewayClient gateway,TimeProvider clock)
{
    private NpgsqlConnection Connection()=>new(mode.SupabaseConnection);
    private static NpgsqlCommand Sql(NpgsqlConnection c,NpgsqlTransaction? tx,string text,params (string,object)[] values)
    {
        var cmd=new NpgsqlCommand(text,c,tx){CommandTimeout=15};
        foreach(var (name,value) in values)cmd.Parameters.AddWithValue(name,value);
        return cmd;
    }
    public IReadOnlyList<OnlinePaymentOption> Options()=>new[] {
        new OnlinePaymentOption("PAYOS","Quét mã QR ngân hàng (payOS)",mode.IsSupabase && mode.OrdersEnabled && settings.Ready("PAYOS")),
        new OnlinePaymentOption("VISA","Thẻ Visa (qua VNPay)",mode.IsSupabase && mode.OrdersEnabled && settings.Ready("VISA")),
        new OnlinePaymentOption("VNPAY","VNPay ngân hàng",mode.IsSupabase && mode.OrdersEnabled && settings.Ready("VNPAY")),
        new OnlinePaymentOption("ZALOPAY","Ví ZaloPay",mode.IsSupabase && mode.OrdersEnabled && settings.Ready("ZALOPAY"))
    };
    private static async Task Installed(NpgsqlConnection c,NpgsqlTransaction? tx,CancellationToken ct)
    {
        await using var cmd=Sql(c,tx,"SELECT to_regprocedure('webstore.settle_gateway_payment(character varying,character varying,character varying,numeric,character varying)') IS NOT NULL");
        if(!Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)))throw new CustomerRequestException(503,"Cấu trúc thanh toán trực tuyến chưa được cập nhật tại cửa hàng.");
    }
    public async Task<PaymentLink> Start(string customer,string order,string ip,CancellationToken ct)
    {
        await using var c=Connection();await c.OpenAsync(ct);await Installed(c,null,ct);
        string method;decimal amount;string attempt;var created=clock.GetUtcNow();
        await using(var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct)) {
            await using(var invoice=Sql(c,tx,"""
                SELECT s.paymentmethod,h.khachphaitra,h.loaihoadon,h.trangthaithanhtoan
                FROM webstore.order_snapshot s JOIN public.hoadon h ON h.sohd=s.sohd AND h.makh=s.makh
                WHERE s.sohd=@id AND s.makh=@customer FOR UPDATE OF h
                """,("id",order),("customer",customer)))
            await using(var r=await invoice.ExecuteReaderAsync(ct)) {
                if(!await r.ReadAsync(ct))throw new CustomerRequestException(404,"Không tìm thấy đơn hàng.");
                method=r.GetString(0);amount=r.GetDecimal(1);
                if(!PaymentGatewaySettings.Online(method) || r.GetString(2)!="WEB_CHO_THANH_TOAN" || r.GetString(3)!="CHUA_THANH_TOAN")
                    throw new CustomerRequestException(409,"Đơn hàng không còn ở trạng thái chờ thanh toán trực tuyến.");
            }
            if(!settings.Ready(method))throw new CustomerRequestException(503,"Phương thức thanh toán chưa được cấu hình tại cửa hàng.");
            PaymentGatewaySettings.Amount(amount);
            if(method=="PAYOS") {
                await using var payos=Sql(c,tx,"SELECT to_regclass('webstore.payos_payment_number') IS NOT NULL");
                if(!Convert.ToBoolean(await payos.ExecuteScalarAsync(ct)))throw new CustomerRequestException(503,"Cửa hàng chưa cập nhật thanh toán QR payOS.");
            }
            await using(var previous=Sql(c,tx,"SELECT payment_url,expires_at,status FROM webstore.gateway_attempt WHERE sohd=@id ORDER BY created_at DESC,id DESC LIMIT 1",("id",order)))
            await using(var r=await previous.ExecuteReaderAsync(ct)) {
                if(await r.ReadAsync(ct)) {
                    if(r.GetString(2) is "PAID" or "REVIEW")throw new CustomerRequestException(409,"Giao dịch đã được tiếp nhận. Vui lòng xem trạng thái đơn hàng.");
                    if(r.GetDateTime(1)>created.UtcDateTime) {
                        if(r.IsDBNull(0))throw new CustomerRequestException(409,"Giao dịch đang được khởi tạo. Vui lòng kiểm tra lại đơn hàng sau ít phút.");
                        return new(r.GetString(0),order);
                    }
                }
            }
            attempt=created.ToOffset(TimeSpan.FromHours(7)).ToString("yyMMdd")+"_"+Guid.NewGuid().ToString("N");
            if(method=="PAYOS") {await using var number=Sql(c,tx,"SELECT nextval('webstore.payos_payment_number')");attempt=Convert.ToInt64(await number.ExecuteScalarAsync(ct)).ToString(System.Globalization.CultureInfo.InvariantCulture);}
            await using var insert=Sql(c,tx,"""
                INSERT INTO webstore.gateway_attempt(id,sohd,provider,method,merchant,amount,created_at,expires_at)
                VALUES(@attempt,@order,@provider,@method,@merchant,@amount,@created,@expires)
                """,("attempt",attempt),("order",order),("provider",method=="PAYOS"?"PAYOS":method=="ZALOPAY"?"ZALOPAY":"VNPAY"),("method",method),
                ("merchant",settings.Merchant(method)),("amount",amount),("created",created.UtcDateTime),("expires",created.AddMinutes(15).UtcDateTime));
            await insert.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
        }
        // The attempt is durable before an external request; a timeout cannot create an unknown duplicate payment.
        var url=await gateway.Create(method,attempt,amount,ip,created,ct);
        await using(var save=method=="PAYOS"?
            Sql(c,null,"UPDATE webstore.gateway_attempt SET payment_url=@url,provider_link_id=@link WHERE id=@id",("url",url),("link",new Uri(url).Segments.Last()),("id",attempt)):
            Sql(c,null,"UPDATE webstore.gateway_attempt SET payment_url=@url WHERE id=@id",("url",url),("id",attempt)))await save.ExecuteNonQueryAsync(ct);
        return new(url,order);
    }
    public async Task<string> Receive(GatewayEvent payment,CancellationToken ct)
    {
        await using var c=Connection();await c.OpenAsync(ct);await Installed(c,null,ct);
        if(payment.Provider=="PAYOS") {
            await using var match=Sql(c,null,"SELECT EXISTS(SELECT 1 FROM webstore.gateway_attempt WHERE id=@id AND provider='PAYOS' AND merchant=@merchant AND provider_link_id=@link)",
                ("id",payment.AttemptId),("merchant",payment.Merchant),("link",(object?)payment.PaymentLinkId??DBNull.Value));
            if(!Convert.ToBoolean(await match.ExecuteScalarAsync(ct)))throw new CustomerRequestException(400,"Giao dịch QR không khớp đơn hàng.");
        }
        if(!payment.Paid) {
            // A browser cancel/failure does not prove that a later asynchronous payment cannot succeed.
            await using var check=Sql(c,null,"SELECT count(*) FROM webstore.gateway_attempt WHERE id=@id AND provider=@provider AND merchant=@merchant AND amount=@amount",
                ("id",payment.AttemptId),("provider",payment.Provider),("merchant",payment.Merchant),("amount",payment.Amount));
            if(Convert.ToInt32(await check.ExecuteScalarAsync(ct))!=1)throw new CustomerRequestException(400,"Giao dịch không khớp.");
            return "PENDING";
        }
        try {
            await using var cmd=Sql(c,null,"SELECT webstore.settle_gateway_payment(@id,@provider,@merchant,@amount,@transaction)",
                ("id",payment.AttemptId),("provider",payment.Provider),("merchant",payment.Merchant),("amount",payment.Amount),("transaction",payment.TransactionId));
            return (string)(await cmd.ExecuteScalarAsync(ct))!;
        }catch(PostgresException e) when(e.SqlState=="P0001") {throw new CustomerRequestException(400,"Giao dịch không khớp đơn hàng.");}
    }
    public async Task PayosNotify(GatewayEvent payment,CancellationToken ct)
    {
        await using var c=Connection();await c.OpenAsync(ct);
        await using var known=Sql(c,null,"SELECT EXISTS(SELECT 1 FROM webstore.gateway_attempt WHERE id=@id AND provider='PAYOS' AND merchant=@merchant AND provider_link_id=@link)",
            ("id",payment.AttemptId),("merchant",payment.Merchant),("link",payment.PaymentLinkId!));
        // payOS sends a signed sample when confirming webhook setup. Other channel orders are not website orders.
        if(!Convert.ToBoolean(await known.ExecuteScalarAsync(ct)))return;
        var verified=await gateway.Query("PAYOS",payment.AttemptId,clock.GetUtcNow(),clock.GetUtcNow(),"127.0.0.1",ct);
        if(verified!=null)await Receive(verified,ct);
    }
    public async Task<string?> ReturnOrder(GatewayEvent? payment,CancellationToken ct)
    {
        if(payment==null)return null;
        await using var c=Connection();await c.OpenAsync(ct);
        await using var cmd=Sql(c,null,"SELECT sohd FROM webstore.gateway_attempt WHERE id=@id AND provider=@provider AND merchant=@merchant AND amount=@amount",
            ("id",payment.AttemptId),("provider",payment.Provider),("merchant",payment.Merchant),("amount",payment.Amount));
        return await cmd.ExecuteScalarAsync(ct) as string;
    }
    public async Task<string> Recheck(string customer,string order,string ip,CancellationToken ct)
    {
        await using var c=Connection();await c.OpenAsync(ct);await Installed(c,null,ct);
        string id,method,merchant,status;DateTime created;
        await using(var cmd=Sql(c,null,"""
            SELECT a.id,a.method,a.merchant,a.created_at,a.status FROM webstore.order_snapshot s
            JOIN webstore.gateway_attempt a USING(sohd) WHERE s.sohd=@order AND s.makh=@customer
            ORDER BY a.created_at DESC,a.id DESC LIMIT 1
            """,("order",order),("customer",customer)))
        await using(var reader=await cmd.ExecuteReaderAsync(ct)) {
            if(!await reader.ReadAsync(ct))throw new CustomerRequestException(404,"Chưa có giao dịch để kiểm tra.");
            id=reader.GetString(0);method=reader.GetString(1);merchant=reader.GetString(2);created=reader.GetDateTime(3);status=reader.GetString(4);
        }
        if(status is "PAID" or "REVIEW")return status;
        if(settings.Merchant(method)!=merchant)throw new CustomerRequestException(409,"Cửa hàng cần kiểm tra cấu hình của giao dịch này.");
        var payment=await gateway.Query(method,id,new DateTimeOffset(created),clock.GetUtcNow(),ip,ct);
        return payment==null?"PENDING":await Receive(payment,ct);
    }
}
