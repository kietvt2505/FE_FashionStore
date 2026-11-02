# Điều chỉnh số lượng và quay lại trang sau xác thực

Ngày: 01/11/2026.

- Giỏ hàng có nút −/+ bên cạnh ô nhập số lượng. Nút giảm dừng ở 1; nút tăng dừng ở tồn kho. Khách vẫn có thể nhập số và bấm **Cập nhật** để lưu, tính lại thành tiền. Kiểm tra tồn kho phía máy chủ được giữ nguyên.
- Khi khách chưa đăng nhập bấm tim ở trang chủ, danh sách hoặc chi tiết sản phẩm, máy chủ lưu sản phẩm và địa chỉ trang hiện tại trong session. Sau xác thực thành công, hệ thống lưu yêu thích rồi mở lại trang đó, giữ query bộ lọc, phân trang, màu và fragment.
- Liên kết đăng nhập/đăng ký và form đăng ký giữ ReturnUrl, kể cả khi nhập sai dữ liệu. Đăng ký thành công tự đăng nhập; nếu bước đăng nhập tự động thất bại thì chuyển sang form đăng nhập, giữ trang quay lại.
- Địa chỉ chuyển hướng sau đăng nhập phải là URL nội bộ; không chuyển khách sang địa chỉ bên ngoài hoặc trang quản trị.

## Kiểm tra

- Build MVC với MvcBuildViews=true: thành công.
- 10 kiểm thử WishlistLoginIntentTests: thành công.
- node --check cho storefront.js: thành công.
- HTTP qua IIS Express riêng và API giả lập: đăng nhập sai rồi thử lại; đăng ký có lỗi rồi thành công; tự đăng nhập và lưu yêu thích; giữ query/fragment; cập nhật giỏ; từ chối 0/vượt tồn kho; từ chối URL bên ngoài/quản trị: thành công.
- Trình duyệt: quay lại trang danh sách số 2; giữ màu Trắng trên trang chi tiết; tăng 1→2 và cập nhật thành tiền; giảm về 1 và vô hiệu hóa nút giảm; vô hiệu hóa nút tăng ở tồn 84; kiểm tra bố cục di động và máy tính.
- Các luồng xác thực được chạy bằng tài khoản/dữ liệu giả lập, không tạo tài khoản hay thay đổi dữ liệu cửa hàng thật.

Minh chứng: evidence/quantity-auth-return-runtime.json và evidence/cart-quantity-controls-desktop.png.
