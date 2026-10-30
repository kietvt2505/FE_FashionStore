# Ưu đãi thành viên website — 27/10/2026

**Cập nhật 30/10/2026:** theo yêu cầu mới, website giữ ưu đãi hạng mặc định và cho nhập thêm một mã toàn đơn trên số tiền còn lại. Xem `CHECKOUT_VOUCHER_CODES.md` và chạy `Supabase-Checkout-Voucher-Codes.sql` để bật. Quy định chỉ giảm theo hạng dưới đây là bản chốt trước cập nhật này.

Người dùng đã chốt: website chỉ tự động giảm theo hạng giống WinForms; không nhập/cộng thêm mã; điểm chỉ tiếp tục tích lũy, chưa đổi thành tiền.

| Tổng chi tiêu trước đơn hiện tại | Hạng | Giảm |
| --- | --- | --- |
| Đến hết 5.000.000đ | Bạc | 0% |
| Trên 5.000.000đ đến hết 15.000.000đ | Vàng | 5% |
| Trên 15.000.000đ | Kim Cương | 10% |

Nguồn quy tắc: `getCustomerRank`, `getCustomerDiscountRate`, `calculateCartSummary` trong `Winform_FashionStore/QLIShopThoiTrang/winform/qlicuahang/js/ung-dung.js`. Hạng tính theo `khachhang.tongchitieu`, không suy ra từ số điểm hoặc tin vào hạng trình duyệt gửi lên. Giảm trên tổng tiền hàng, làm tròn đến đồng như POS. Ví dụ giỏ 200.000đ, hạng Vàng giảm 10.000đ, khách trả 190.000đ. Không đổi giá sản phẩm trong catalog.

## Áp dụng

1. Trong Supabase SQL Editor, chạy toàn bộ `database/Supabase-Membership-Pricing.sql` sau migration đơn hàng. File bao gồm tạo bảng lưu giá nếu chưa có, không cần bật mã ưu đãi website.
2. Khởi động lại backend bằng lệnh đang dùng:

```powershell
.\scripts\Start-SupabaseCatalog.ps1 -EnableOrders -OrderEmployeeId "NV-001" -EnableBankTransfer
```

3. Tải lại website. Checkout tự báo hạng, điểm hiện có, tỷ lệ và số tiền sau giảm. Trang Ưu đãi chỉ hướng dẫn áp mã tại cửa hàng. API từ chối mã được gửi thủ công.

Migration chỉ thêm thông tin lưu tại thời điểm đặt hàng; giữ hóa đơn cũ, giá, tồn, điểm và chương trình khuyến mãi chung. Các đơn có mã ưu đãi từ phiên bản trước vẫn đọc được trong lịch sử. Nếu khách đủ điều kiện giảm mà chưa chạy migration, hệ thống chặn tạo đơn thay vì thu đủ giá.

## Dữ liệu giao cho WinForms

- `hoadon.tongcong`: tiền hàng trước ưu đãi; `chietkhau`: số tiền giảm theo hạng.
- `khachphaitra`: tiền sau giảm; `tiencod`: tiền sau giảm với COD; `makm`: NULL cho đơn web mới.
- `webstore.order_pricing`: lưu hạng, tỷ lệ, điểm hiện có và tiền giảm tại thời điểm đặt. Không tính lại ưu đãi đơn cũ theo hạng mới.
- Backend kiểm tra lại thông tin tài khoản/giá/tồn trong giao dịch tạo đơn. Dữ liệu tiền, hạng hoặc điểm gửi giả từ trình duyệt không ảnh hưởng kết quả.
- WinForms tiếp nhận số tiền đã chốt từ hóa đơn; không áp hạng lần hai cho đơn web. Không thay mã WinForms bên ngoài workspace trong đợt này.

Điểm vẫn theo luồng đang có: chuyển khoản cộng khi nhân viên xác nhận đúng số tiền sau giảm; COD do WinForms xử lý lúc giao thành công. Không trừ điểm để đổi tiền. Đặt đơn chưa tự cộng điểm hoặc trừ kho; cơ chế giữ hàng COD vẫn là phần cần phối hợp riêng.

## Kiểm tra

89 kiểm thử đạt, 15 kiểm thử SQL Server bỏ qua. PostgreSQL riêng đã kiểm tra: mốc chi tiêu chính xác, làm tròn, từ chối mã, không tin tiền/hạng/điểm client, lưu tiền COD, giá/hạng cũ khi retry, kiểm tra hạng lại lúc đặt, chuyển khoản đúng tiền và xác nhận lặp không cộng điểm/trừ kho hai lần. MVC/Razor biên dịch thành công. Trình duyệt bản thử xác nhận checkout tự hiển thị Kim Cương 10%, giỏ 200.000đ còn 180.000đ và không có ô nhập mã.

Người dùng đã xác nhận chạy SQL thành công trên Supabase chung ngày 27/10/2026. Agent không tạo giao dịch thử trên Supabase chung; cần backend nạp phiên bản mới để dùng chính sách này.
