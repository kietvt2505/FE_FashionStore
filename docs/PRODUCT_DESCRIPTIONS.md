# Mô tả dưới ảnh sản phẩm

- Desktop: khung mô tả ngắn nằm ngay dưới gallery, cùng độ rộng với cột ảnh.
- Mobile: gallery trước, phần chọn mua tiếp theo và mô tả sau đó.
- Mô tả dài có phần đọc đầy đủ, mặc định thu gọn để giữ bố cục.

## Nguồn nội dung

1. `public.sanpham.mota`: văn bản riêng của sản phẩm, bỏ qua SKU ẩn và nội dung trống. Nếu nhiều SKU có nội dung khác nhau, website dùng nội dung không trống đầu tiên theo mã SKU. Partner nên đồng bộ mô tả giữa các SKU cùng `masp`.
2. `public.catalog_product.description`: giữ tương thích nội dung website đã nhập trước đây.
3. Nếu cả hai trống, FE dùng nội dung biên tập theo tên/loại sản phẩm trong `FashionStore.Web/Helpers/ProductDescriptionHelper.cs`, kèm gợi ý chất liệu, phối đồ và bảo quản. Không gán tỉ lệ thành phần hay công năng kỹ thuật như thông tin đã được xác nhận.

`ProductMaterialHelper` phân biệt ba nguồn: **chất liệu theo tên**, **kiểu vải theo tên** và **chất liệu gợi ý**. Cotton, Modal, Polyester/Polyeste hoặc Linen ghi rõ trong tên được giữ theo tên, không tự thêm tỉ lệ. Jeans/Denim được hiển thị là **kiểu vải**, không gán mặc định thành 100% cotton.

FE hiển thị đề xuất theo tên/loại với tiêu đề gọn **“Chất liệu tham khảo”**. Đã bỏ dòng “Chưa xác nhận” và chú thích dài trên giao diện; hai nhãn đơn được rút gọn thành **Cotton** và **Polyester**. Đây là nội dung biên tập tham khảo, không phải dữ liệu thành phần đã kiểm định; không tự thêm tỉ lệ “100%” hoặc ghi đề xuất vào CSDL. Khi partner nhập mô tả riêng, website ưu tiên mô tả đó và ẩn phần gợi ý.

| Nhóm theo tên | Hướng chất liệu gợi ý |
|---|---|
| Áo thun, baby tee, polo, sơ mi, đồ mặc trong thường ngày | Cotton |
| Thể thao, thun lạnh, vớ thể thao | Polyester |
| Hoodie, nỉ bông | Nỉ Cotton / Polyester |
| Quần tây, vest, blazer | Polyester / Viscose |
| Co giãn, ôm body | Vải pha Elastane, chưa gán sợi nền |
| Không đường may | Nylon / Elastane |
| Áo gió, dù, bomber | Polyester dệt thoi |
| Trench coat, túi hộp, kaki | Cotton / Polyester dệt thoi |
| Váy, đầm không ghi chất liệu | Polyester |
| Cardigan, đồ len | Sợi dệt kim Acrylic pha; không khẳng định len động vật |
| Áo khoác lông | Lông tổng hợp Polyester, chưa xác nhận |
| Mũ thường ngày / beret | Cotton dệt thoi / vải dạ pha |
| Vớ thường ngày | Cotton pha sợi đàn hồi |

Quy tắc xét tên rõ trước loại chung: Cotton trong tên thắng nhóm thể thao; Denim thắng áo khoác; “thun lạnh” không được nhận nhầm thành Linen; “tay dài” không được nhận nhầm thành quần tây. Tên thử nghiệm, tên trống, phủ định Cotton/Polyester và sản phẩm không đủ ngữ cảnh không được gán chất liệu gợi ý.

Mỗi nhóm có đoạn giới thiệu, hai ý về cấu trúc/đặc điểm tham khảo và cách bảo quản riêng. Hướng dẫn trên nhãn luôn được ưu tiên; không tự thêm chứng nhận, thông số chống UV, chống nước, kháng khuẩn hoặc tỉ lệ sợi.

Điểm nhấn thiết kế (tay dài, cổ V, lưng cao, ống rộng...) được lấy từ chính tên, tối đa hai ý để giữ khung mô tả gọn. Khi có mô tả riêng trong CSDL, nội dung tự động về chất liệu và thiết kế được ẩn để tránh mâu thuẫn với dữ liệu đã nhập.

Chạy `database/Supabase-Product-Descriptions.sql` một lần trong Supabase SQL Editor để bổ sung cột `mota`. Script chạy lại được, không sửa nội dung cũ. Website vẫn hoạt động trước khi thêm cột nhờ phần đọc tương thích schema cũ.

Mô tả là văn bản thuần, được Razor mã hóa khi hiển thị; không dùng HTML từ database. Có thể nhập nhiều đoạn bằng ký tự xuống dòng. WinForms chưa có ô nhập mô tả trong bản hiện tại; partner có thể bổ sung ô nhập và cập nhật `mota` cho các SKU cùng `masp` sau.
