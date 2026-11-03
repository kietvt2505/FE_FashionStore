using FashionStore.Backend.Data;
using FashionStore.Backend.Middleware;
using Npgsql;
namespace FashionStore.Backend.Services;
// Existing QR transactions remain owned by the partner's payment workflow.
public sealed class OnlinePaymentService {
 private readonly DatabaseMode mode;
 public OnlinePaymentService(DatabaseMode mode,PaymentGatewaySettings settings,PaymentGatewayClient gateway,TimeProvider clock){this.mode=mode;}
 public IReadOnlyList<OnlinePaymentOption> Options()=>Array.Empty<OnlinePaymentOption>();
 public Task<PaymentLink> Start(string customer,string order,string ip,CancellationToken ct)=>throw new CustomerRequestException(409,"Vui lòng thanh toán bằng COD hoặc chuyển khoản. Giao dịch QR hiện do cửa hàng quản lý.");
 public Task<string> Receive(GatewayEvent payment,CancellationToken ct)=>throw new CustomerRequestException(409,"Giao dịch QR cần được xử lý qua hệ thống thanh toán của cửa hàng.");
 public Task PayosNotify(GatewayEvent payment,CancellationToken ct)=>throw new CustomerRequestException(409,"Giao dịch QR cần được xử lý qua hệ thống thanh toán của cửa hàng.");
 public Task<string?> ReturnOrder(GatewayEvent? payment,CancellationToken ct)=>Task.FromResult<string?>(null);
 public async Task<string> Recheck(string customer,string order,string ip,CancellationToken ct){
 await using var c=new NpgsqlConnection(mode.SupabaseConnection);await c.OpenAsync(ct);await using var cmd=new NpgsqlCommand("SELECT q.trangthai FROM public.giaodichthanhtoanqr q JOIN public.hoadon h USING(sohd) WHERE h.makh=@kh AND h.sohd=@id AND h.nguondon='WEB'",c);cmd.Parameters.AddWithValue("kh",customer);cmd.Parameters.AddWithValue("id",order);
 return await cmd.ExecuteScalarAsync(ct) as string??throw new CustomerRequestException(404,"Chưa có giao dịch QR cho đơn hàng.");
 }
}