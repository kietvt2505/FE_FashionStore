# Mã đơn hàng web ngắn

Đơn mới dùng `WEB-000001`, `WEB-000002`… Đây là mã hóa đơn thật, dùng chung trên website, Supabase, WinForms và nội dung chuyển khoản. Mã cũ giữ nguyên để bảo toàn liên kết chi tiết, thanh toán, điểm, đánh giá và đổi trả.

## Áp dụng

1. Chạy toàn bộ `database/Supabase-Short-Order-Numbers.sql` trong SQL Editor sau migration đơn hàng. Kết quả cuối phải có `webstore.web_order_number` và `co_khoa_chong_trung=true`.
2. Khởi động lại backend bằng lệnh đang dùng. Đơn mới nhận mã ngắn; lịch sử vẫn mở được cả mã cũ và mã mới.

Script thêm bộ đếm dùng chung và khóa chống trùng theo khách hàng + mã yêu cầu. Không đổi mã hóa đơn đang có, không sửa giá, tồn, thanh toán hoặc điểm. Chạy lại chỉ đưa bộ đếm tiến lên nếu cần; không đặt lại về 1. Không cần sửa cấu trúc bảng hóa đơn của partner.

## Chống trùng và tương thích

- Bấm đặt nhiều lần hoặc gửi lại cùng yêu cầu trả về đúng đơn đã tạo. Mã yêu cầu được lưu riêng, không phải mã hóa đơn hiển thị.
- Hai tài khoản có cùng mã yêu cầu vẫn tạo hai đơn khác nhau, chỉ đọc được đơn thuộc mình.
- Bộ đếm PostgreSQL cấp số dùng chung cho nhiều tiến trình website. Kiểm tra thêm các mã đã nhập thủ công trong `hoadon` và các bản lưu đơn trước khi dùng số mới.
- Mã cũ dạng WEB + chuỗi dài vẫn được tra cứu khi gửi lại yêu cầu cũ, kể cả hóa đơn chờ đã bị xóa ở WinForms nhưng bản lưu website còn giữ.
- Giữ tiền tố WEB để bộ lọc nguồn đơn của WinForms hiện tại nhận đúng nguồn website. WinForms dùng trạng thái/loại hóa đơn để phân luồng COD hoặc chuyển khoản.
- Bộ đếm có thể có khoảng trống khi giao dịch thất bại; không coi số thứ tự là tổng số đơn. Trên 999999, mã tăng thành `WEB-1000000`, không cắt bớt chữ số.
- Nếu chưa chạy SQL, backend mới chặn tạo đơn mới thay vì tạo một mã không có khóa chống trùng; vẫn trả được đơn cũ khi gửi lại yêu cầu đã xử lý.

## Kiểm chứng

94 kiểm thử đạt, 15 kiểm thử SQL Server bỏ qua. PostgreSQL riêng đã kiểm tra tạo COD/chuyển khoản bằng mã ngắn, gửi đồng thời, gửi lại đơn cũ/mới, hai khách có cùng mã yêu cầu, giữ quyền sở hữu, chạy migration nhiều lần, tránh mã có sẵn/nhập tay và định dạng khi vượt sáu chữ số. Không đổi mã đơn hoặc tạo đơn thử trên Supabase chung trong lượt sửa này.
