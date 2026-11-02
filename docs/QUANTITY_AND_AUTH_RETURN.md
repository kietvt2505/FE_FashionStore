# Điều chỉnh số lượng và quay lại trang sau xác thực

Ngày: 01/11/2026. Cập nhật giao diện số lượng: 02/11/2026.

- Giỏ hàng dùng ô số lượng dạng number, có mũi tên tăng/giảm bên phải như trang chi tiết sản phẩm. Ô số lượng và nút **Cập nhật** nằm trên cùng một hàng ở cả máy tính và điện thoại; tồn kho hiển thị bên dưới. Mũi tên giới hạn từ 1 đến tồn kho. Khách vẫn có thể nhập số và bấm **Cập nhật** để lưu, tính lại thành tiền. Kiểm tra số nguyên và tồn kho phía máy chủ được giữ nguyên.
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

## Kiểm tra giao diện ngày 02/11/2026

- Render Razor trực tiếp trên IIS Express của web chính và kiểm tra bằng giỏ khách, không ghi vào giỏ tài khoản hay tạo đơn thật.
- Desktop và viewport điện thoại: ô số lượng/nút Cập nhật có cùng tọa độ hàng; không tràn ngang màn hình điện thoại.
- Vớ cổ trung, tồn 6: tăng 1→2, giảm dừng ở 1, tăng dừng ở 6; cập nhật số lượng 2 cho thành tiền 118.000đ; nhập 7 bị chặn và hiện thông báo. Đã đưa số lượng thử lại về 1.
- JavaScript storefront.js qua node --check thành công.
- Minh chứng lưu ở máy: evidence/cart-inline-number-desktop.png và evidence/cart-inline-number-verification.json.
