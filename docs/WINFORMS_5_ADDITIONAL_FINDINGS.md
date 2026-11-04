# Đối chiếu 5 vấn đề trong nội dung được dán

Đọc source WinForms commit `6d11b10` tại `Winform_FashionStore/QLIShopThoiTrang`. Đây là các vấn đề bổ sung so với 10 mục đồng bộ web đã xử lý. Không chạy API/initializer của form trên Supabase để thử, không sửa source form khi chưa đổi phạm vi được cho phép.

| Mục | Đối chiếu source hiện tại | Phần cần sửa |
|---|---|---|
| 1. GET dữ liệu nội bộ chưa bắt đăng nhập | `api/QLIShopThoiTrang.Api/Program.cs:36` chỉ chặn phần lớn thao tác ghi và một số đường GET. `GET /api/app-state` và `GET /api/hoa-don` không thuộc danh sách đó. `StoreRepository.GetAppStateAsync` còn trả đối tác, lịch sử bán, nhập và giao hàng. | API form: xác thực mọi GET nội bộ, sau đó kiểm tra vai trò và giới hạn dữ liệu trả về. Không chỉ khóa nút giao diện. |
| 2. CORS rộng | `Program.cs:12` gọi `AllowAnyOrigin/AllowAnyHeader/AllowAnyMethod`. | API form/WebView2: xác định origin thực tế rồi giới hạn CORS. UI hiện có trang file/WebView2 nên không đổi danh sách origin tùy tiện. CORS không thay xác thực. |
| 3. Ghi đè tồn khi sửa sản phẩm | `StoreRepository.cs:351` trong `SaveProductAsync` còn cập nhật cả `SoLuong` và `SoLuongTonKho` bằng dữ liệu gửi lên khi trùng SKU. Endpoint POST `/api/san-pham` vẫn gọi hàm này. | API form: tách cập nhật thông tin khỏi biến động tồn. **Cần phân biệt:** màn hình quản lý mới gọi `/api/san-pham/nhom/{productId}` → `UpdateProductGroupAsync`, hiện không ghi tồn. Vì vậy rủi ro đã xác nhận nằm ở đường upsert cũ còn mở, không phải mọi thao tác sửa tên trên màn hình mới đều làm sai tồn. |
| 4. Tự sửa database khi khởi động | `Program.cs:28` gọi `SqlServerDatabaseInitializer.InitializeAsync`; initializer có ALTER/UPDATE, sửa khóa/index và backfill cờ tồn. | API form: tách migration khỏi khởi động, giữ bước kiểm tra schema/connection. Những cờ lịch sử chưa chắc chắn phải đối chiếu nghiệp vụ. |
| 5. PAYOS web chưa hoàn chỉnh | UI và `OnlinePaymentService.Options` đã không đưa lựa chọn online vào checkout; `Start` từ chối. Trước bản rà soát này, `SupabaseOrderService.PlaceAsync` vẫn nhận PAYOS khi cấu hình đủ. | Website: chặn mọi phương thức online chưa hỗ trợ **trước khi mở kết nối/tạo hóa đơn/giữ kho**. Chỉ COD và chuyển khoản theo phạm vi hiện tại. Không thay đổi PayOS POS. |

## Thứ tự xử lý phía form

1. Chặn GET nội bộ chưa đăng nhập; không trả dữ liệu ngoài vai trò dù người dùng sửa URL hoặc trạng thái UI.
2. Bảo vệ đường upsert SKU cũ khỏi ghi tồn từ form stale; kiểm kê/nhập/xuất dùng giao dịch riêng và kiểm tra phiên bản khi phù hợp.
3. Giới hạn CORS theo nguồn WebView2 thực tế cùng với kiểm tra token.
4. Chuyển DDL và chuyển đổi dữ liệu sang migration riêng; khởi động chỉ kiểm tra cấu trúc đã triển khai.

Đây là đánh giá source. Không kết luận API đang mở Internet: cấu hình mặc định dùng loopback 127.0.0.1:5001; các lớp mạng/trình duyệt cũng có thể hạn chế truy cập. Việc sửa CORS đơn lẻ không xử lý lỗi quyền đọc.

## Kiểm tra phần web

`NativePaymentAvailabilityTests` bật đủ cấu hình PayOS giả lập nhưng để connection string cố tình không hợp lệ. Gửi PAYOS/VISA/VNPAY/ZALOPAY phải nhận 409 rõ ràng ngay trước bước kết nối database. Kiểm thử không gọi cổng thanh toán, không tạo hóa đơn và không trừ tồn thật.

Các mục 1–4 cần thay đổi source API form. Chỉ sửa website không thể khóa GET, đổi CORS hoặc ngăn initializer/upsert trên tiến trình API form khác. Chưa triển khai thay đổi backend web của mục 5 cho tới khi khởi động lại bản mới; những phần đồng bộ 10 mục trước vẫn ở bản đã áp dụng `2294beb`.
