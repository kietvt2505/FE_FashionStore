# Phối màu xanh navy và trắng

Phối màu được chọn để đồng bộ với WinForms và tăng độ rõ khi trình chiếu. Nguồn màu tập trung tại `FashionStore.Web/Content/navy-theme.css`, nạp sau CSS giao diện và CSS riêng của trang chủ.

| Vai trò | Màu |
| --- | --- |
| Nút chính, giá, điểm nhấn, footer | `#142D4E` |
| Tiêu đề và chữ chính | `#102238` |
| Chữ mô tả, chú thích, placeholder | `#344256` |
| Nền trang, thẻ và form | `#FFFFFF` |
| Nền phụ | `#F2F5FA` |
| Trạng thái được chọn | `#E2EBF6` |
| Viền khối | `#9AACC3` |
| Viền ô nhập và nút phụ | `#5D7189` |
| Chữ phụ trên nền navy | `#E8EFF8` |
| Lỗi | `#971C32` trên `#FFF1F3` |
| Thành công/còn hàng | `#205B3A` trên `#E9F5EE` |

Chữ mô tả trên trang chủ tăng từ 11–13px lên 15px với độ đậm 500. Giá sản phẩm 16px, độ đậm 700; tên danh mục và nhãn nhỏ 12px trở lên. Không làm nhạt toàn bộ nút hết size: dùng chữ đậm màu, nét đứt và gạch để phân biệt.

Chữ phụ `#344256` có tương phản khoảng 10,20:1 trên trắng và 9,33:1 trên nền phụ; chữ trắng trên nút navy khoảng 13,86:1. Các tỷ lệ này là tính toán màu màn hình, không phải đo trên thiết bị máy chiếu thực tế.

Áp dụng cho trang chủ, menu, sản phẩm, tài khoản, giỏ, thanh toán, đơn hàng, các trang hỗ trợ và layout quản trị. Bố cục, các luồng nghiệp vụ, nội dung ảnh sản phẩm, các ô màu sản phẩm và ảnh QR ngân hàng không đổi. Giữ màu đỏ/xanh lá có ý nghĩa báo lỗi/thành công.

Sau khi cập nhật, tải lại bằng Ctrl+F5 để nhận CSS mới. Không có thay đổi database hay yêu cầu chạy SQL.
