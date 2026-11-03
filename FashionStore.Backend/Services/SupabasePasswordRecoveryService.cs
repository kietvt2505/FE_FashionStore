using FashionStore.Backend.Middleware;
namespace FashionStore.Backend.Services;
public sealed class SupabasePasswordRecoveryService {
 public Task Request(string email,CancellationToken ct)=>throw new CustomerRequestException(410,"Khôi phục mật khẩu đang tạm ngưng. Vui lòng liên hệ cửa hàng.");
 public Task Reset(string token,string password,CancellationToken ct)=>throw new CustomerRequestException(410,"Khôi phục mật khẩu đang tạm ngưng. Vui lòng liên hệ cửa hàng.");
}