# Gửi và xem đánh giá sản phẩm

- Chỉ khách có đơn của chính mình đã giao thành công và thanh toán mới được gửi đánh giá. BE kiểm tra lại điều kiện khi gửi; mỗi sản phẩm trong một đơn chỉ gửi một lần.
- Đánh giá hợp lệ được lưu trực tiếp với trạng thái `HIEN_THI` và xuất hiện công khai ngay, không qua bước chờ duyệt. BE ghi trạng thái tường minh, không phụ thuộc default cũ của CSDL. `TU_CHOI` được giữ để tương thích các bài đã bị ẩn trước đây và không được công khai.
- Danh sách công khai, **Đánh giá của bạn** và form viết đánh giá được hiển thị ngay bên dưới phần chi tiết/mô tả sản phẩm tại `/Product/Details/{id}#product-reviews`. Nút **Xem và viết đánh giá** chỉ cuộn đến mục này; liên kết **Viết đánh giá** từ đơn hàng cũng mở đúng sản phẩm và chọn sẵn đơn đủ điều kiện. URL `/Customer/Reviews/{id}` cũ chuyển về mục đánh giá trên chi tiết để giữ liên kết đã lưu.
- Sau khi gửi, website quay lại đúng mục đánh giá trên chi tiết sản phẩm và tải lại **Đánh giá của bạn** qua `GET /api/customer/reviews/{id}/mine`. Endpoint yêu cầu tài khoản khách hàng, lấy mã khách từ phiên xác thực và chỉ trả đánh giá của người đó cho sản phẩm đang xem; không nhận mã khách từ trình duyệt. Phân trang giữ màu, liên kết quay lại danh sách và đơn đang chọn.
- Mục riêng hiển thị nội dung, số sao, mã đơn, thời điểm gửi và trạng thái hiển thị. Mã đơn chỉ trả về cho tác giả, không có trong API danh sách công khai. Mỗi trang tối đa 10 đánh giá của người dùng và 10 đánh giá công khai.
- Cả mục riêng và danh sách công khai hiển thị họ tên tác giả từ `public.khachhang.hoten`, liên kết bằng `product_review.makh`. Tên trống dùng “Khách hàng”; không lấy tên của người đang xem bài. API không kèm số điện thoại, email, địa chỉ hoặc tên đăng nhập. Tên được Razor mã hóa khi hiển thị; cập nhật hồ sơ sẽ cập nhật tên bên cạnh các bài đã viết, không sửa nội dung đánh giá.
- Nếu chưa tải được đánh giá riêng, trang báo lỗi cụ thể và vẫn hiển thị phần công khai; không báo nhầm rằng bài chưa được gửi.
- Khách chưa đăng nhập vẫn xem được đánh giá công khai. Form chỉ xuất hiện khi API trả đơn đủ điều kiện; lỗi kiểm tra điều kiện không mở form. Đăng nhập từ mục đánh giá quay về đúng sản phẩm và vị trí này. Nếu phiên hết hạn khi gửi, yêu cầu đăng nhập lại và giữ nội dung/số sao; không tự động gửi bài thay khách.
- Thông báo thành công/lỗi nằm tại mục đánh giá. Gửi lỗi giữ nội dung, số sao và đơn đang chọn; gửi thành công tải lại danh sách và loại đơn đã đánh giá khỏi form. Trang chi tiết có `Cache-Control: no-store` do chứa đánh giá riêng và đơn của khách. Trên điện thoại, form nằm trước danh sách đánh giá.

## Kiểm tra giao diện ngày 02/11/2026

- Build MVC với `MvcBuildViews=true`: thành công, gồm partial đánh giá và các liên kết từ đơn hàng.
- 15 kiểm thử tự động: 10 trường hợp điều kiện đơn đã giao/thanh toán, 5 trường hợp tải đánh giá công khai/riêng, xác thực và lỗi dịch vụ.
- 10 luồng HTTP trên IIS Express riêng với API/tài khoản giả: khách xem công khai; đăng nhập quay lại mục đánh giá; phiên hết hạn giữ bản nháp; đơn chưa nhận không có form; đơn đã nhận được chọn sẵn; phân trang giữ ngữ cảnh; lỗi gửi giữ nội dung/số sao; gửi thành công hiển thị tại chi tiết và không gửi lặp; khách khác xem bài công khai; liên kết cũ chuyển về chi tiết; lỗi điều kiện không làm mất trang sản phẩm. Kết quả chi tiết trong `evidence/inline-reviews-runtime/results.json` tại máy kiểm tra.
- Trình duyệt trên web chính: mục đánh giá nằm dưới chi tiết/mô tả; nút chỉ đổi fragment của cùng trang. Viewport điện thoại 375px: form trước danh sách, không tràn ngang. Luồng gửi thử chỉ dùng dữ liệu giả, không tạo đánh giá/tài khoản/đơn trên Supabase.

Chạy `database/Supabase-Reviews-Auto-Publish.sql` một lần trên Supabase hiện có để đổi default và chuyển các bài `CHO_DUYET` cũ sang `HIEN_THI`, sau khi đối chiếu đúng người mua, sản phẩm, đơn đã giao và thanh toán. Bài gắn đơn hủy/chưa giao/chưa thanh toán và bài `TU_CHOI` không được mở. File chạy lại được, giữ nội dung, số sao và ngày gửi; không sửa đơn, tồn kho hay điểm. Các cài đặt mới dùng default `HIEN_THI` trong `Supabase-Customer-Features.sql`.

Cần khởi động lại backend sau khi cập nhật mã. Không cần gửi lại các đánh giá cũ đã được cập nhật bởi script.

Kiểm thử PostgreSQL local dùng dữ liệu giả: gửi thành công và thấy ngay từ tài khoản khác/khách chưa đăng nhập; không lộ mã đơn; endpoint riêng vẫn yêu cầu đúng tài khoản; giới hạn phân trang; điều kiện đã giao và chống gửi lặp. Migration được thử trên bài hợp lệ, bài gắn đơn hủy/chưa giao/chưa trả tiền, bài đã ẩn và chạy lặp; nội dung và ngày gửi không thay đổi.
