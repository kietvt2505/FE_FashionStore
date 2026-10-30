# Hoàn thiện mua sắm trên website

## Cài đặt

Sau các file catalog, tài khoản/giỏ hàng và ưu đãi thành viên đã chạy trước đây, chạy toàn bộ `database/Supabase-Storefront-Enhancements.sql` trong Supabase → SQL Editor. File được kiểm tra trên PostgreSQL riêng; chạy lại được và không tự áp dụng giảm giá cho sản phẩm.

Khởi động lại backend và website để nạp mã mới. Giữ các tùy chọn bật đơn hàng và chuyển khoản đang dùng.

Kiểm tra cấu trúc bằng truy vấn chỉ đọc:

```sql
SELECT to_regclass('webstore.product_media') AS bang_anh,
       to_regclass('webstore.product_discount_price') AS bang_gia,
       to_regclass('webstore.guest_cart_merge') AS bang_gop_gio;
SELECT column_name FROM information_schema.columns
WHERE table_schema='webstore' AND table_name='catalog_product' AND column_name='description';
```

## Kết nối phần giảm giá với partner WinForms

Partner cần bổ sung ô nhập phần trăm giảm theo mã sản phẩm và gọi hàm bằng tài khoản backend có quyền phù hợp, dùng tham số Npgsql:

```sql
SELECT webstore.set_product_discount(@ma_san_pham, @phan_tram);
```

- Phần trăm từ 0 đến dưới 100. Giá sau giảm làm tròn đến đồng. Giá sau giảm phải lớn hơn 0.
- Ví dụ giảm 15% cho SP-001: tham số `ma_san_pham=SP-001`, `phan_tram=15`. Gửi 0 để kết thúc giảm giá và khôi phục giá gốc.
- Hàm lưu giá gốc theo SKU, cập nhật `public.sanpham.dongia` và giá gốc hiển thị trên web trong cùng một giao dịch. Gửi lại cùng phần trăm không làm giảm chồng.
- Website đọc giá này cho thẻ sản phẩm, giỏ hàng, thanh toán và trang chủ. Nhãn phần trăm, giá gốc gạch ngang và giá sau giảm xuất hiện khi có giảm giá hợp lệ. Trang chủ hiển thị tối đa 4 sản phẩm khuyến mãi và liên kết tới toàn bộ danh sách.
- Nếu WinForms sửa giá bán trực tiếp khi chương trình còn chạy, hàm coi giá đã sửa là giá gốc mới ở lần áp dụng tiếp theo. Partner nên kết thúc chương trình bằng 0 trước khi sửa giá gốc để tránh hiểu nhầm.
- Ưu đãi thành viên của website tiếp tục tính trên giá bán hiện tại, theo quy tắc đã thống nhất. Không nhập mã giảm giá toàn đơn trên website.
- Khi kết thúc chương trình, gọi hàm với 0; hiện chưa có lịch tự hết hạn. WinForms phải tải lại dữ liệu để thấy thay đổi.

Đây là cấu trúc kết nối đã chuẩn bị. Chức năng nhập phần trăm ở WinForms vẫn cần partner bổ sung; website không tự tạo giảm giá giả.

## Ảnh nhiều góc và mô tả

Tải ảnh thật lên Storage trước, rồi lưu URL HTTPS và màu đúng tên đang có trong `sanpham.mausac` vào `webstore.product_media`. Mỗi góc là một dòng. `color=NULL` dùng cho ảnh chung. Không cần nhân bản ảnh theo size.

```sql
-- Ví dụ cấu trúc, thay URL bằng ảnh thật của nhóm trước khi chạy.
INSERT INTO webstore.product_media(masp,color,url,alt,sort_order)
VALUES ('SP-001','Đỏ','https://YOUR-PROJECT.supabase.co/storage/v1/object/public/product-images/catalog/SP-001/do-mat-sau.png','Áo thun đỏ — mặt sau',10);

UPDATE webstore.catalog_product
SET description='Mô tả thực tế do nhóm cung cấp.' WHERE masp='SP-001';
```

Website vẫn đọc ảnh SKU hiện có, loại trùng cùng màu/cùng URL, rồi bổ sung các góc trong bảng mới. Ảnh có `active=false` không hiển thị. Di chuột trên ảnh lớn để phóng to; bàn phím Enter/Space và thao tác chạm cũng hỗ trợ. Mô tả hiển thị dưới dạng văn bản, không thực thi HTML.

Bảng size áo và quần gồm 4 hàng mỗi bảng là tham khảo chung; không phải thông số riêng được xác nhận cho từng mẫu. Thông tin chất liệu/chất lượng cần nhóm điền đúng thực tế. Gợi ý sản phẩm cùng danh mục, phụ kiện và màu khác dùng dữ liệu đang bán.

## Giỏ hàng khách

- Khách chưa đăng nhập thêm, sửa và xóa giỏ hàng được. Giỏ lưu trong phiên trình duyệt; hết phiên hoặc xóa cookie có thể mất giỏ. Chưa đồng bộ giữa thiết bị.
- Sau đăng nhập, hộp thoại giữa màn hình hỏi có gộp giỏ không. Đồng ý mới gửi SKU/số lượng lên backend; giá và tồn kho được đọc lại từ database.
- Gộp là một giao dịch, có mã chống cộng trùng khi gửi lại. Nếu một dòng không còn đủ hàng thì toàn bộ lần gộp được giữ lại để khách xử lý, không gộp một phần.
- Chọn “Không thêm” xóa giỏ tạm, giữ nguyên giỏ tài khoản. Giỏ hàng không giữ chỗ tồn kho.
- Khi nhập vượt tồn kho ở giỏ hàng và nhấn Cập nhật, số lượng được chọn toàn bộ để nhập lại. Backend vẫn kiểm tra tồn kho khi cập nhật và đặt hàng.
- Liên kết quay lại giữ URL danh sách, bộ lọc và trang số đã xem trong cùng phiên. Mỗi lần mở danh sách mới sẽ cập nhật trang quay lại.

Nếu nhấn đồng ý mà gộp chưa được, hộp thoại hiển thị nguyên nhân, cho thử lại hoặc chọn “Để sau, giữ giỏ tạm”. Giỏ tạm chưa gộp vẫn xuất hiện ở đầu trang giỏ hàng; tải lại trang không tự mở hộp thoại liên tục. “Không thêm” mới là thao tác bỏ giỏ tạm.

Lỗi 503 có thể do chưa chạy file SQL mới. Kiểm tra bằng câu SELECT cuối `Supabase-Storefront-Enhancements.sql`: cả `co_bang_gop_gio` và `co_ham_gop_gio` phải là `true`. Nếu thiếu, chạy lại toàn bộ file trong SQL Editor của đúng dự án Supabase, rồi thử gộp lại. Khi đủ cấu trúc mà vẫn lỗi, cần xem dòng lỗi đầu tiên của backend; phần cuối stack trace không xác định được lỗi kết nối/quyền cụ thể.

## Visa, ZaloPay, VNPay — bổ sung sau

Theo lựa chọn của nhóm, chưa cấu hình cổng thanh toán ở đợt này. Ba lựa chọn đã có trên giao diện, được khóa và ghi “Chưa khả dụng”. Backend từ chối các phương thức này; không tạo hóa đơn hoặc đánh dấu đã trả tiền khi chưa kết nối.

COD và chuyển khoản đối soát thủ công tiếp tục dùng như trước. Không nhập số thẻ, CVV hoặc khóa bí mật vào website/chat.

Để mở thanh toán trực tuyến cần làm tiếp:

1. Nhóm cấu hình tài khoản thử nghiệm và khóa trong nơi lưu bí mật ở backend, cung cấp URL HTTPS cho callback và trang quay lại.
2. Tích hợp tạo giao dịch, xác minh chữ ký callback, đối chiếu số tiền/mã đơn và chống xử lý lặp; trang quay lại từ trình duyệt không được tự xác nhận thanh toán.
3. Kiểm thử thành công, thất bại, hủy, hết hạn, callback lặp và chậm. Chỉ bật lựa chọn sau khi đối soát đúng với WinForms, tồn kho và điểm thành viên.

Tài liệu chính thức: [VNPay](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html), [ZaloPay](https://developer.zalopay.vn/v2/general/overview.html). Visa cần kênh chấp nhận thẻ được nhà cung cấp bật cho tài khoản merchant; có tài khoản thử nghiệm không mặc nhiên đủ quyền cho mọi phương thức.
