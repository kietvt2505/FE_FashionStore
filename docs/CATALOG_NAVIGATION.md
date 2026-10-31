# Menu và bộ lọc theo danh mục hiện tại

Menu Nam/Nữ lấy các danh mục đang hoạt động từ API, giữ đúng mã `category` và `audience` trong đường dẫn. Không ghép thêm `kind` có thể mâu thuẫn với danh mục. Không hiển thị những loại cố định chưa tồn tại trong database.

Menu Phụ kiện chỉ lấy danh mục thuộc phụ kiện như Nón/Mũ, Vớ, Kính mát, Dây nịt, Balo và Túi xách. Đồ thể thao và đồ lót nằm trong nhóm trang phục, không nằm trong Phụ kiện. Danh mục dành riêng cho nữ không xuất hiện trong menu Nam và ngược lại. Phụ kiện dùng chung xuất hiện ở cả hai.

Backend phân loại **khi đọc**, theo cùng bộ quy tắc `shared/CatalogTaxonomy.cs` dùng bởi menu:

1. Danh mục cụ thể hiện tại là nguồn ưu tiên, ví dụ `Quần tây nam` → `NAM / quan-dai`, `Áo hoodie nam` → `NAM / ao-hoodie`.
2. Danh mục trung tính và rộng như `Áo`, `Quần` vẫn dùng phân loại chi tiết đã gán trong `webstore.catalog_product`. Không đoán giới tính từ tên sản phẩm chung chung.
3. Sản phẩm có SKU đang hoạt động thuộc nhiều danh mục khác nhau không được tự gán một giới tính/loại; cần nhóm sửa dữ liệu nguồn.

Cách này xử lý được trường hợp partner thay bộ sản phẩm và dùng lại mã `SP-xxx` khiến bảng gán cũ không còn khớp. Phân loại được áp dụng trước khi đếm và phân trang, và dùng nhất quán trên danh sách/chi tiết. Không sửa bảng nguồn, giá, tồn kho, khuyến mãi hoặc đơn hàng; không cần thêm SQL.

Bộ lọc danh mục trên trang sản phẩm chỉ hiện các lựa chọn phù hợp đối tượng. Đổi đối tượng sẽ bỏ lựa chọn danh mục trái nhóm. Trang Phụ kiện vẫn giữ phạm vi Phụ kiện khi đổi danh mục hoặc chọn “Tất cả danh mục”.

Với danh mục mới chưa nằm trong bộ quy tắc, bổ sung ánh xạ trong `CatalogTaxonomy` hoặc tiếp tục gán thủ công qua `webstore.catalog_product`; không tự nhận một danh mục lạ là Phụ kiện.
