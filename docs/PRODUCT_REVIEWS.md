# Gửi và xem đánh giá sản phẩm

- Chỉ khách có đơn của chính mình đã giao thành công và thanh toán mới được gửi đánh giá. BE kiểm tra lại điều kiện khi gửi; mỗi sản phẩm trong một đơn chỉ gửi một lần.
- Đánh giá hợp lệ được lưu trực tiếp với trạng thái `HIEN_THI` và xuất hiện công khai ngay, không qua bước chờ duyệt. BE ghi trạng thái tường minh, không phụ thuộc default cũ của CSDL. `TU_CHOI` được giữ để tương thích các bài đã bị ẩn trước đây và không được công khai.
- Sau khi gửi, website chuyển về trang đánh giá và tải lại **Đánh giá của bạn** qua `GET /api/customer/reviews/{id}/mine`. Endpoint yêu cầu tài khoản khách hàng, lấy mã khách từ phiên xác thực và chỉ trả đánh giá của người đó cho sản phẩm đang xem; không nhận mã khách từ trình duyệt.
- Mục riêng hiển thị nội dung, số sao, mã đơn, thời điểm gửi và trạng thái hiển thị. Mã đơn chỉ trả về cho tác giả, không có trong API danh sách công khai. Mỗi trang tối đa 10 đánh giá của người dùng và 10 đánh giá công khai.
- Cả mục riêng và danh sách công khai hiển thị họ tên tác giả từ `public.khachhang.hoten`, liên kết bằng `product_review.makh`. Tên trống dùng “Khách hàng”; không lấy tên của người đang xem bài. API không kèm số điện thoại, email, địa chỉ hoặc tên đăng nhập. Tên được Razor mã hóa khi hiển thị; cập nhật hồ sơ sẽ cập nhật tên bên cạnh các bài đã viết, không sửa nội dung đánh giá.
- Nếu chưa tải được đánh giá riêng, trang báo lỗi cụ thể và vẫn hiển thị phần công khai; không báo nhầm rằng bài chưa được gửi.

Chạy `database/Supabase-Reviews-Auto-Publish.sql` một lần trên Supabase hiện có để đổi default và chuyển các bài `CHO_DUYET` cũ sang `HIEN_THI`, sau khi đối chiếu đúng người mua, sản phẩm, đơn đã giao và thanh toán. Bài gắn đơn hủy/chưa giao/chưa thanh toán và bài `TU_CHOI` không được mở. File chạy lại được, giữ nội dung, số sao và ngày gửi; không sửa đơn, tồn kho hay điểm. Các cài đặt mới dùng default `HIEN_THI` trong `Supabase-Customer-Features.sql`.

Cần khởi động lại backend sau khi cập nhật mã. Không cần gửi lại các đánh giá cũ đã được cập nhật bởi script.

Kiểm thử PostgreSQL local dùng dữ liệu giả: gửi thành công và thấy ngay từ tài khoản khác/khách chưa đăng nhập; không lộ mã đơn; endpoint riêng vẫn yêu cầu đúng tài khoản; giới hạn phân trang; điều kiện đã giao và chống gửi lặp. Migration được thử trên bài hợp lệ, bài gắn đơn hủy/chưa giao/chưa trả tiền, bài đã ẩn và chạy lặp; nội dung và ngày gửi không thay đổi.
