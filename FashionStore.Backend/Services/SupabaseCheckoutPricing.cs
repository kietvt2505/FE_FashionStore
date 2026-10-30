using FashionStore.Backend.Middleware;
using Npgsql;

namespace FashionStore.Backend.Services;

internal static class SupabaseCheckoutPricing
{
    internal sealed record Price(decimal Subtotal, decimal Discount, decimal Total, string? Voucher,
        string Rank, int Rate, int Points, bool MembershipInstalled,decimal MemberDiscount,decimal VoucherDiscount);

    // Same strict thresholds as WinForms getCustomerRank/getCustomerDiscountRate.
    internal static (string Rank, int Rate) Membership(decimal totalSpent) => totalSpent > 15000000m
        ? ("Kim Cương", 10) : totalSpent > 5000000m ? ("Vàng", 5) : ("Bạc", 0);

    internal static Task<bool> MembershipInstalled(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken ct) =>
        ReadMembershipInstalled(connection, transaction, ct);

    private static async Task<bool> ReadMembershipInstalled(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT count(*)=4 FROM information_schema.columns
            WHERE table_schema='webstore' AND table_name='order_pricing'
              AND column_name IN ('member_rank','member_rate','member_points','member_discount')
            """,connection,transaction);
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct));
    }

    internal static async Task<bool> IsInstalled(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT to_regclass('webstore.order_pricing') IS NOT NULL", connection, transaction);
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct));
    }

    internal static async Task<bool> VouchersInstalled(NpgsqlConnection connection,NpgsqlTransaction? transaction,CancellationToken ct)
    {
        await using var cmd=new NpgsqlCommand("SELECT to_regclass('webstore.checkout_voucher') IS NOT NULL",connection,transaction);
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct));
    }

    private static async Task<(string Code,decimal Discount)> Voucher(NpgsqlConnection connection,NpgsqlTransaction transaction,
        string voucher,decimal subtotal,decimal remaining,CancellationToken ct)
    {
        if(!await VouchersInstalled(connection,transaction,ct))
            throw new CustomerRequestException(503,"Cửa hàng chưa bật áp dụng mã giảm giá trực tuyến. Vui lòng thử lại sau.");
        await using var cmd=new NpgsqlCommand("""
            SELECT v.makm,v.kind,v.value,v.minimum,v.active,
                (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh')::date BETWEEN v.starts_on AND v.ends_on,
                v.product_promotion
            FROM webstore.checkout_voucher v JOIN public.khuyenmai k ON k.makm=v.makm
            WHERE upper(btrim(v.makm))=upper(btrim(@code)) FOR SHARE OF k
            """,connection,transaction);
        cmd.Parameters.AddWithValue("code",voucher.Trim());
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct))throw new CustomerRequestException(409,"Mã giảm giá không tồn tại. Vui lòng kiểm tra lại.");
        var code=reader.GetString(0);var kind=reader.IsDBNull(1)?null:reader.GetString(1);
        var value=reader.IsDBNull(2)?0:reader.GetDecimal(2);var minimum=reader.GetDecimal(3);
        if(reader.IsDBNull(4)||!reader.GetBoolean(4)||reader.IsDBNull(5)||!reader.GetBoolean(5))
            throw new CustomerRequestException(409,"Mã giảm giá chưa bắt đầu, đã hết hạn hoặc đã ngừng áp dụng.");
        if(reader.GetBoolean(6))throw new CustomerRequestException(409,"Mã này áp dụng riêng cho sản phẩm và đã được tính vào giá bán, không dùng thêm cho toàn đơn.");
        if(value<=0||minimum<0||kind==null||(kind=="PHAN_TRAM"&&value>=100))
            throw new CustomerRequestException(409,"Mã giảm giá chưa có mức giảm hợp lệ cho website.");
        if(subtotal<minimum)throw new CustomerRequestException(409,"Đơn hàng chưa đạt giá trị tối thiểu của mã giảm giá.");
        if(await reader.ReadAsync(ct))throw new CustomerRequestException(409,"Mã giảm giá chưa xác định duy nhất. Vui lòng liên hệ cửa hàng.");
        var discount=Math.Round(kind=="PHAN_TRAM"?remaining*value/100m:value,0,MidpointRounding.AwayFromZero);
        if(discount<=0||discount>=remaining)
            throw new CustomerRequestException(409,"Mã giảm giá không áp dụng được cho giá trị đơn hiện tại. Tổng thanh toán phải lớn hơn 0đ.");
        return(code,discount);
    }

    internal static async Task<Price> Calculate(NpgsqlConnection connection, NpgsqlTransaction transaction,
        bool installed, string customer, string? voucher, decimal subtotal, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT COALESCE(tongchitieu,0),COALESCE(diemtichluy,0)
            FROM public.khachhang WHERE makh=@customer AND trangthai=true
            FOR SHARE
            """, connection, transaction);
        cmd.Parameters.AddWithValue("customer", customer);
        decimal spent; int points;
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new CustomerRequestException(401, "Vui lòng đăng nhập lại.");
            spent=reader.GetDecimal(0);points=Math.Max(0,reader.GetInt32(1));
        }
        var member=Membership(spent);
        var snapshotReady=installed && await MembershipInstalled(connection,transaction,ct);
        if(member.Rate>0 && !snapshotReady)
            throw new CustomerRequestException(503,"Ưu đãi thành viên chưa được cập nhật tại cửa hàng. Vui lòng thử lại sau.");
        // POS uses Math.round on the whole subtotal, not separately on each SKU.
        var memberDiscount=Math.Round(subtotal*member.Rate/100m,0,MidpointRounding.AwayFromZero);
        string? code=null;decimal voucherDiscount=0;
        if(!string.IsNullOrWhiteSpace(voucher)) {
            if(voucher.Trim().Length>20)throw new CustomerRequestException(400,"Mã giảm giá tối đa 20 ký tự.");
            if(!snapshotReady)throw new CustomerRequestException(503,"Cửa hàng chưa cập nhật lưu ưu đãi đơn hàng. Vui lòng thử lại sau.");
            (code,voucherDiscount)=await Voucher(connection,transaction,voucher,subtotal,subtotal-memberDiscount,ct);
        }
        var discount=memberDiscount+voucherDiscount;
        return new(subtotal,discount,subtotal-discount,code,member.Rank,member.Rate,points,snapshotReady,memberDiscount,voucherDiscount);
    }
}
