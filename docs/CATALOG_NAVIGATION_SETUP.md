# Bộ sưu tập Nam, Nữ và Phụ kiện

Menu và trang chủ dùng ba nhóm Nam, Nữ, Phụ kiện. Menu mở bằng hover, nút trên điện thoại, bàn phím và đóng bằng Escape. Đường dẫn Trẻ em cũ được chuyển sang Phụ kiện; dữ liệu TRE_EM cũ vẫn được giữ trong database.

## Đường dẫn

- Nam: `/Product?audience=NAM`.
- Nữ: `/Product?audience=NU`.
- Quần ngắn nam: `/Product?audience=NAM&kind=quan-ngan`.
- Phụ kiện, không giới hạn giới tính: `/Product?kind=phu-kien`.
- Kính mát: `/Product?kind=kinh-mat`; Dây nịt: `/Product?kind=day-nit`.
- Phụ kiện riêng của Nam/Nữ vẫn chọn được trong menu tương ứng.
- Khuyến mãi: `/Product?saleOnly=true&sort=price_asc`, dành cho mọi đối tượng.

Bộ lọc và phân trang kết hợp audience, kind, category, giá, kích cỡ, màu và tồn kho. UNISEX xuất hiện ở cả Nam và Nữ. Sản phẩm chưa phân loại vẫn có ở trang tất cả sản phẩm, chưa vào bộ sưu tập Nam/Nữ. Riêng sản phẩm hoặc danh mục có tên chính xác “123” (kể cả khoảng trắng hai đầu) được loại khỏi toàn bộ catalog, chi tiết và danh sách SKU. Danh mục này cũng được lọc ở MVC để không xuất hiện trong ô chọn khi backend còn phiên bản cũ. Việc loại diễn ra trước đếm và phân trang; không xóa dữ liệu trong database chung. Một mã sản phẩm chỉ hiện một thẻ dù có nhiều SKU.

## Áp dụng lên Supabase

1. Nếu chưa có bảng `webstore.catalog_product`, chạy toàn bộ `database/Supabase-Catalog-Merchandising.sql` trong SQL Editor. File này tạo bảng và gợi ý loại từ tên rõ nghĩa, chưa gán đối tượng.
2. Chạy toàn bộ `database/Supabase-Catalog-Assignments.sql`. File đã bao gồm cập nhật ràng buộc loại mới nên database đã có bảng không cần chạy lại bước 1.
3. Khởi động lại backend để nhận bộ lọc Phụ kiện có Dây nịt và tải lại website.

File phân loại dựa trên 23 mã sản phẩm đọc từ API thật ngày 27/10/2026, theo yêu cầu người dùng tự phân loại:

| Mã | Phân loại | Loại |
| --- | --- | --- |
| SP-001–SP-006 | Nam và nữ | Áo thun, sơ mi, polo, khoác gió, hoodie, vest |
| SP-007–SP-011 | Nam và nữ | Quần dài kaki/jean, quần ngắn kaki/jeans, jogger |
| SP-012–SP-016 | Nam và nữ | Kính mát, kính râm, mũ, dây nịt, vớ |
| SP-017–SP-020 | Nữ | Váy ngắn/dài, đầm ngắn/dài |
| SP-021–SP-022 | Nam và nữ | Áo khác, hoodie có lông |
| SP-023 | Chưa phân loại; ẩn trên website | Sản phẩm thử tên “123” |

Các tên áo/quần không thể hiện giới tính được xếp dùng chung để có thể duyệt ở cả Nam và Nữ. Đây là phân loại biên tập ban đầu, nhóm có thể điều chỉnh từng mã khi có thông tin giới tính chính thức. Không tự phân loại sản phẩm mới chỉ bằng suy đoán từ tên.

Nếu dữ liệu chưa bị nhóm thay đổi, sau khi chạy sẽ có **18 sản phẩm ở Nam, 22 ở Nữ và 5 phụ kiện**; 5 phụ kiện cũng nằm trong hai nhóm Nam/Nữ. Hai mẫu quần ngắn xuất hiện trong từng nhóm. Các danh mục Giày/Dép/Balo/Túi xách chưa có sản phẩm tương ứng sẽ hiện trạng thái trống.

SQL dừng nếu mã hoặc tên 22 sản phẩm đã thay đổi; chỉ điền audience/kind khi audience đang NULL, giữ phân loại nhóm đã gán. Chạy lại an toàn. Không sửa bảng sản phẩm, giá gốc, giá bán, tồn kho hoặc đơn hàng. Không xóa sản phẩm thử “123”. Giá gốc chỉ nhập khi có căn cứ, không tạo giá giảm giả.

## Điều chỉnh sau này

Schema `webstore`, bảng `catalog_product`:
- `audience`: NAM, NU, UNISEX hoặc NULL; TRE_EM giữ để tương thích dữ liệu cũ.
- `kind`: ao-thun, ao-polo, ao-so-mi, ao-khoac, ao-hoodie, ao-vest, ao-khac, quan-ngan, quan-dai, quan-jogger, vay-ngan, vay-dai, dam-ngan, dam-dai, giay, dep, kinh-mat, mu, vo, day-nit, balo, tui-xach.
- `regular_price`: giá gốc thật; NULL nếu không giảm. Giá bán vẫn dùng sanpham.dongia.

Có thể xuất bảng phân loại để tự chỉnh bằng `.\scripts\Export-CatalogClassification.ps1`; file `tmp/catalog-classification.html` cho phép chọn rồi tải SQL, không tự ghi vào Supabase. Trước khi xuất SQL với loại mới, áp dụng bản mới của migration hoặc file phân loại ở trên.

## Kiểm chứng

MVC/Razor biên dịch thành công. Trang local `/Product?kind=phu-kien` trả HTTP 200, có menu Phụ kiện, không còn menu Trẻ em. Kiểm thử PostgreSQL thử riêng bao gồm lọc Nam/Nữ, UNISEX, phụ kiện, dây nịt, gộp SKU, phân trang, giá/tồn kho không đổi, chạy lại SQL, bảo toàn phân loại nhập tay và từ chối mã sản phẩm đã đổi tên.

Người dùng đã chạy file phân loại thành công trên Supabase ngày 27/10/2026. Kiểm tra API thật và trang MVC: Nam 18, Nữ 22, quần ngắn Nam/Nữ mỗi nhóm 2, Dây nịt 1; tất cả trang trả HTTP 200 và không có SP-023 trong các nhóm này. Backend đang chạy vẫn dùng bộ lọc phụ kiện cũ nên trang tổng phụ kiện hiện 4; cần khởi động lại backend để nạp bộ lọc mới có Dây nịt, đạt 5 sản phẩm. Agent không tự ghi vào database chung.
