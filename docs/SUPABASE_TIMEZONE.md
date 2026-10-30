# Giờ đặt hàng trên website

Supabase lưu các cột `timestamp without time zone` của luồng website dưới dạng giờ UTC. Phiên bản cũ trả số giờ này không có dấu múi giờ, khiến trang lịch sử hiển thị 06:24 thay vì 13:24 giờ Việt Nam.

## Quy tắc

- Kết nối Supabase của backend đặt múi giờ phiên làm việc là UTC. Không đổi múi giờ toàn database hoặc máy của partner.
- Ghi ngày lập hóa đơn/bản lưu đơn bằng `CURRENT_TIMESTAMP AT TIME ZONE 'UTC'`.
- Khi đọc, chuyển các timestamp sự kiện đã lưu theo UTC thành giá trị có múi giờ (`AT TIME ZONE 'UTC'`). JSON của API có hậu tố `Z` để chỉ thời điểm UTC.
- Website định dạng các thời điểm này theo UTC+7. Không phụ thuộc múi giờ máy chủ chạy MVC.
- Giờ địa phương không kèm múi giờ từ hợp đồng SQL Server cũ giữ nguyên, tránh cộng 7 tiếng hai lần. Các ngày lịch như ngày kết thúc chương trình không bị coi là thời điểm UTC.

Áp dụng cho lịch sử/chi tiết đơn, tiến trình xử lý, lịch sử điểm, ngày đánh giá và yêu cầu đổi trả. Trang quản trị dùng cùng bộ định dạng. Cột giao thành công do mã WinForms hiện có ghi bằng `CURRENT_TIMESTAMP` trên Supabase được đọc theo UTC.

## Sử dụng

Khởi động lại backend rồi tải lại website. Không cần chạy thêm SQL và không cộng bù 7 tiếng vào các dòng hóa đơn đã lưu. Đơn `WEB-000001` có ngày gốc `2026-10-27 06:24` sẽ hiển thị `27/10/2026 13:24` trên website.

## Kiểm tra

Kiểm thử API xác nhận ngày đặt là UTC và JSON có hậu tố Z; cả danh sách và chi tiết định dạng 06:24 UTC thành 13:24 Việt Nam. Kiểm thử thêm việc qua ngày lúc 20:30 UTC, đầu vào đã mang +07:00 và ngày địa phương cũ không bị đổi hai lần. Kiểm thử database dùng phiên UTC giống Supabase; không sửa dữ liệu thật trong đợt này.
