# Màu sắc và ảnh nhiều góc sản phẩm

## Hoạt động trên website

- Thẻ sản phẩm dùng `colorOptions` từ API: mỗi màu một lựa chọn, cộng tồn kho các size; đổi màu sẽ đổi ảnh, giá và đường dẫn chi tiết.
- Đường dẫn chi tiết có `color`, ví dụ `/Product/Details/SP-001?color=Đỏ`. Mở lại hoặc tải lại trang vẫn chọn màu đó. Màu hết hàng vẫn xem được ảnh nhưng không thêm vào giỏ.
- Gallery chỉ hiển thị ảnh của màu đang chọn, loại URL trùng do khác size. Có ít nhất bốn ô góc chụp; góc chưa có ảnh hoặc ảnh không tải được để trống. Không tự lấy ảnh của màu khác.
- Ảnh có thể chuyển bằng thumbnail, nút trước/sau hoặc phím mũi tên. Zoom bằng di chuột, chạm hoặc Enter trên ảnh lớn; ô trống không zoom.

## Dữ liệu để WinForms kết nối

Giữ ảnh chính ở `public.sanpham.hinhanh` của từng SKU. Ảnh nhiều góc dùng bảng **đã có** `webstore.product_media` trong `Supabase-Storefront-Enhancements.sql`:

| Cột | Nội dung |
| --- | --- |
| `masp` | Mã sản phẩm hiện có |
| `color` | Tên màu trùng `public.sanpham.mausac` |
| `url` | URL HTTPS ảnh đã tải lên Storage |
| `alt` | Mô tả góc chụp bằng tiếng Việt |
| `sort_order` | Thứ tự các ảnh bổ sung |
| `active` | `true` để hiển thị, `false` để ẩn |

Lưu từng góc thành một dòng với cùng `masp` và `color`. Ảnh SKU hiển thị trước, sau đó ảnh bổ sung theo `sort_order` và `id`. `color = NULL` chỉ dành cho ảnh chủ động dùng chung mọi màu. Không tạo dòng ảnh giả để lấp ô trống.

Thay đổi này không cần chạy thêm SQL nếu bảng trên đã được tạo. Khởi động lại backend và tải lại website để nhận API và giao diện mới. Không sửa dữ liệu giá, tồn kho hoặc thanh toán.
