# Mã giảm giá website — 30/10/2026

## Bật tính năng

Chạy toàn bộ `database/Supabase-Checkout-Voucher-Codes.sql` sau `Supabase-Membership-Pricing.sql` trong Supabase SQL Editor. Hai cột `co_ma_giam_gia` và `co_luu_giam_theo_ma` phải cùng true. Khởi động lại backend và tải lại website bằng Ctrl + F5.

Migration tạo view đọc mã từ `public.khuyenmai`, hàm phân biệt khuyến mãi SKU và cột tính riêng số tiền giảm theo mã trong snapshot giá. Không sửa mã khuyến mãi của partner, giá sản phẩm, tổng tiền đơn cũ hay số dư khách hàng. Các đối tượng mới không cấp quyền PUBLIC/anon/authenticated.

## Cách tính đã chốt

1. Giá trong giỏ đã bao gồm khuyến mãi riêng từng sản phẩm từ `khuyenmaisanpham`.
2. Giảm theo hạng tự động: Bạc 0%, Vàng 5%, Kim Cương 10%, tính từ tổng chi tiêu thực trong database.
3. Nếu khách nhập một mã toàn đơn hợp lệ, giảm thêm trên số tiền còn lại sau bước 2. Mã phần trăm làm tròn đến đồng; mã số tiền trừ đúng số tiền giảm.

Ví dụ tạm tính 200.000đ, hạng Vàng giảm 10.000đ, mã 10% giảm thêm 19.000đ, còn 171.000đ. Hai dòng giảm được hiển thị riêng trên trang thanh toán và chi tiết đơn. Không cộng thẳng phần trăm hạng với phần trăm mã.

Điều kiện giá trị đơn tối thiểu dùng **tạm tính sản phẩm sau giảm giá SKU, trước giảm hạng/mã**. Một đơn dùng một mã. Chưa hỗ trợ đơn thanh toán bằng 0đ; mã làm số tiền còn lại bằng hoặc nhỏ hơn 0 bị từ chối.

## Kiểm tra mã tại backend

- Mã trong `khuyenmai`, đang hoạt động, có kiểu Phần trăm / PHAN_TRAM / % hoặc Số tiền / SO_TIEN / VND hợp lệ.
- Ngày bắt đầu và kết thúc tính theo ngày Việt Nam, bao gồm toàn bộ ngày kết thúc, thống nhất với giá khuyến mãi SKU hiện có.
- Bỏ dấu cách ở đầu/cuối và không phân biệt hoa thường khi tra; lưu mã gốc đúng trong database. Nếu có hai mã chỉ khác hoa/thường, yêu cầu cửa hàng sửa để xác định duy nhất.
- Nếu mã có dòng liên kết trong `khuyenmaisanpham`, không nhập lại như mã toàn đơn. Trang Ưu đãi giải thích rằng ưu đãi đó đã nằm trong giá sản phẩm và dẫn đến danh sách hàng giảm giá.
- Database chung hiện chỉ có điều kiện chương trình chung và tối thiểu đơn; không tự tạo giới hạn lượt hoặc quyền dùng riêng từng khách khi chưa có dữ liệu/quy tắc tương ứng.

Bấm Áp dụng chỉ báo giá, không tạo đơn, không đổi điểm/kho và không cần nhập xong địa chỉ trước. Áp mã sai sẽ hiện lỗi và báo lại số tiền theo hạng, không giữ số giảm của mã trước. Bấm Bỏ mã quay về ưu đãi hạng. Thông tin người nhận/địa chỉ đã điền được giữ lại.

## Lưu đơn và thanh toán

Khi xác nhận đặt hàng, backend đọc lại giỏ, hạng và mã dưới transaction; nếu partner tắt mã hoặc thay đổi điều kiện sau lần báo giá, đơn bị từ chối thay vì dùng giá cũ trên trình duyệt. Chỉ gửi mã từ website, không chấp nhận số tiền giảm/tổng tiền do trình duyệt tính.

- `public.hoadon.makm`: mã đúng đã áp dụng.
- `public.hoadon.chietkhau`: tổng giảm hạng và mã.
- `public.hoadon.khachphaitra` / `tiencod`: tổng phải trả sau hai loại giảm.
- `webstore.order_pricing`: tổng tiền, mã, giảm hạng đã chốt. `voucher_discount` tự tính từ `discount - member_discount`, giữ đúng cả đơn cũ.

Thanh toán/chuyển khoản/QR sử dụng số tiền đã lưu trên hóa đơn. Sửa hoặc hết hạn mã sau khi đặt không đổi giá đơn cũ. Gửi lại cùng RequestId trả lại đơn cũ, không tạo thêm đơn hoặc áp mã lần hai. Quy trình hủy, hoàn tiền và tích điểm vẫn dựa trên tổng thực trả.

## Giao diện

Trang Checkout có ô Mã giảm giá, nút Áp dụng, nút Bỏ mã và lỗi theo điều kiện thực. Trang Customer/Promotions có nút Dùng khi thanh toán cho mã toàn đơn; tham số voucher được backend kiểm tra lại, không lấy tiền giảm từ đường dẫn.
