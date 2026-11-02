# Kiểm tra public và webstore — 02/11/2026

**Cập nhật sau kiểm tra:** đã chuyển sang public và xóa schema webstore rỗng
sau khi người dùng xác nhận. Backend/web chính đã chạy và hậu kiểm đạt. Xem
[kết quả migration](PUBLIC_SCHEMA_MIGRATION.md). Nội dung audit dưới đây ghi
nhận trạng thái trước khi chuyển để giữ dấu vết đối chiếu.

## Phạm vi và giới hạn

Đã đối chiếu mã nguồn và đọc trực tiếp metadata bằng giao diện Supabase trong
Edge, project `Khoa Luan Cu Nhan`, ref `oagcfzcslkkiokbmkrlg`, branch `main`.
Chưa sửa mã ứng dụng, chưa chạy SQL trên Supabase, chưa chuyển bảng/schema và
chưa thay quyền truy cập. Không đọc nội dung riêng tư của các bản ghi khách hàng.
Backend localhost:5000 không phản hồi lúc kiểm tra; không có cấu hình kết nối
Supabase trong môi trường Process/User của phiên kiểm tra. Kết nối thực tế giữa
hai máy vẫn cần đối chiếu; metadata dưới đây được đọc từ dashboard đã đăng nhập.

## Xác minh trực tiếp trên Supabase

Trong cùng project, bộ chọn schema có cả `public` và `webstore`.
Trang Database Tables hiển thị 19 đối tượng ở public và 24 ở webstore (bộ đếm
giao diện ghi "tables" nhưng danh sách có cả view). Các số ROWS trên trang này
là ước lượng thống kê, không dùng làm số lượng bản ghi chính xác.

- Public có `sanpham`, `loaisanpham`, `khachhang`, `hoadon`, `chitiethoadon`,
  `phieudoitra`, `chitietdoitra` và các bảng quản lý của partner.
- Webstore có `address`, `cart`, `cart_line`, `catalog_product`,
  `checkout_voucher`, `customer_account`, `customer_refund_queue`,
  `daily_order_number`, `gateway_attempt`, `gateway_receipt`, `guest_cart_merge`,
  `order_cancellation`, `order_line_snapshot`, `order_pricing`, `order_refund`,
  `order_snapshot`, `password_reset`, `payment_confirmation`,
  `product_discount_price`, `product_media`, `product_review`, `return_request`,
  `sku_price`, `wishlist`.
- Đã mở metadata `webstore.return_request`: 12 cột gồm `sophieudt`, `sohd`,
  `makh`, `masku`, `quantity`, `kind`, `newsku`, `reason`, `createdat`, `status`,
  `rejectionreason`, `handled_return_id`; `sohd` và `makh` có khóa ngoại.
- Các bảng nghiệp vụ public và các bảng bổ sung webstore tồn tại trong cùng
  database của project đang xem. Đây là hai schema, không phải hai database
  được chứng minh bởi hai mục này.

Liên kết kiểm tra: https://supabase.com/dashboard/project/oagcfzcslkkiokbmkrlg/database/tables

## Kết quả từ source

Các service Supabase cùng lấy kết nối từ `DatabaseMode.SupabaseConnection`:
ưu tiên `ConnectionStrings:Supabase`, sau đó `YODYSTORE_CONNECTION_STRING`.
`Program.cs` dùng giá trị đó cho cả catalog và tài khoản/giỏ hàng trong chế độ
Supabase. Không thấy cấu hình tách kết nối Supabase riêng cho đổi trả/hủy đơn.

`database/Supabase-Customer-Cart.sql` tạo **schema webstore**, không tạo database
Supabase thứ hai. Schema là nhóm đối tượng bên trong database. Cần đối chiếu
project/host/database thực tế ở máy web và máy partner để xác nhận cùng kết nối.

| Chức năng | Nguồn trong source web | Tình trạng dùng chung với partner |
| --- | --- | --- |
| Sản phẩm, danh mục | public.sanpham, public.loaisanpham | Đã đọc bảng public |
| Hồ sơ khách hàng | public.khachhang | Đã dùng public; tài khoản web nằm ở webstore.customer_account |
| Đặt hàng | public.hoadon, public.chitiethoadon; thông tin bổ sung trong webstore.order_snapshot và order_line_snapshot | Cùng giao dịch trong một kết nối |
| Hủy đơn | Hàm webstore.cancel_customer_order; cập nhật public.hoadon, lưu lịch sử ở webstore.order_cancellation | Thay đổi trạng thái hóa đơn public |
| Yêu cầu đổi/trả từ web | webstore.return_request | Chưa ghi vào public.phieudoitra/public.chitietdoitra khi gửi yêu cầu |
| Đổi/trả trên bản source form | PhieuDoiTra, ChiTietDoiTra (tên không chỉ định schema) | Danh sách chỉ lấy TrangThai='HOAN_TAT'; tạo phiếu xử lý hoàn tất |

Điểm lệch quan trọng: yêu cầu chờ duyệt của website và phiếu đổi/trả đã xử lý của
form là hai mô hình/bảng khác nhau. `return_request` có `handled_return_id`, nhưng
trong source đã kiểm tra chưa có luồng ghi nhận xử lý để liên kết yêu cầu đó với
phiếu native. Chuyển nó thành `public.return_request` không làm truy vấn đọc
`public.phieudoitra` của form tự nhìn thấy yêu cầu này.

## Điều cần kiểm tra trước khi chuyển về public

1. Kiểm kê sâu database/project thực tế bằng
   `database/Inspect-Supabase-Schemas-ReadOnly.sql` trên đúng project đang dùng.
   Script chỉ đọc metadata trong transaction READ ONLY và kết thúc bằng ROLLBACK.
2. So sánh host/project/database của web và form, không chia sẻ mật khẩu.
3. Kiểm tra bảng/view/function/sequence trùng tên trong public, khóa ngoại,
   trigger, function body, quyền và RLS. Source hiện gọi rõ `webstore.*` ở nhiều
   nơi; chuyển đối tượng mà chưa cập nhật source/function sẽ làm hỏng các luồng đó.
4. Thống nhất mô hình yêu cầu chờ duyệt và phiếu đổi/trả hoàn tất trước khi đổi
   nơi lưu. Không tự chuyển yêu cầu chờ duyệt thành phiếu HOAN_TAT: điều đó có thể
   tác động tồn kho, hoàn tiền và điểm khi partner xử lý.
5. Nếu chốt tất cả đối tượng bổ sung nằm ở public: chuẩn bị migration giữ dữ liệu,
   cập nhật đồng bộ mapping/SQL/function/trigger và kiểm thử trên bản sao database,
   kèm backup và rollback. Không chỉ đổi search_path vì source dùng schema tường minh.

Việc đặt bảng trong schema public không đồng nghĩa mở quyền đọc/ghi công khai.
Các bảng tài khoản, reset mật khẩu và dữ liệu riêng của khách phải giữ quyền/RLS
phù hợp sau khi chuyển schema.

## File nguồn đã đối chiếu

- FashionStore.Backend/Data/DatabaseMode.cs
- FashionStore.Backend/Program.cs
- FashionStore.Backend/Data/SupabaseCatalogDbContext.cs
- FashionStore.Backend/Data/CustomerPostgresMapping.cs
- FashionStore.Backend/Services/SupabaseOrderService.cs
- FashionStore.Backend/Services/SupabaseCustomerFeaturesService.cs
- database/Supabase-Customer-Cart.sql
- database/Supabase-Customer-Features.sql
- database/Supabase-Customer-Order-Cancellation.sql và các bản cập nhật hủy/hoàn tiền
- Source partner: api/QLIShopThoiTrang.Api/Data/StoreRepository.cs,
  các hàm GetReturnsAsync/CreateReturnAsync
