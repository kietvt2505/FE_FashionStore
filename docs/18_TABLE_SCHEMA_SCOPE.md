# Giới hạn 18 bảng theo sơ đồ đã chốt

**Yêu cầu mới nhất:** vẫn giữ 18 bảng gốc nhưng người dùng đã cho phép bổ sung các cột tiếng Việt phù hợp chức năng. Đã áp dụng 19 cột trên 4 bảng; Supabase xác nhận vẫn 18 bảng, dữ liệu cột gốc giữ nguyên. Đánh giá và khôi phục mật khẩu đã được nối lại bằng cột mới (gửi email cần cấu hình SMTP). Xem [NATIVE18_COLUMNS.md](NATIVE18_COLUMNS.md). Các đoạn dưới ghi lại phạm vi trước khi cho phép bổ sung cột.

Người dùng xác nhận ngày 03/11/2026: chỉ dùng đúng 18 bảng gốc và giữ nguyên cả cột theo sơ đồ. Không tạo bảng thay thế trong schema khác, không thêm cột, không dùng cột ghi chú/mật khẩu để chứa dữ liệu chức năng không liên quan.

## 18 bảng gốc

`baocaodoanhthu`, `bienbanxulyhangloi`, `chitietbaocaodoanhthu`, `chitietdoitra`, `chitiethoadon`, `chitietnhapkho`, `giaodichthanhtoanqr`, `hoadon`, `khachhang`, `khuyenmai`, `khuyenmaisanpham`, `loaisanpham`, `nhanvien`, `nhasanxuat`, `phanquyen`, `phieudoitra`, `phieunhapkho`, `sanpham`.

Cấu trúc đối chiếu: `database/schema-reference/Supabase_public_metadata_20261103.json`. View giá khuyến mãi hiện có không phải bảng nghiệp vụ bổ sung.

## Đối chiếu chức năng

| Chức năng | Bảng gốc phù hợp | Phạm vi cần xử lý |
|---|---|---|
| Danh mục, biến thể, tồn kho, ảnh, mô tả | sanpham, loaisanpham, nhasanxuat | Đọc trực tiếp các cột hiện có |
| Tài khoản, hồ sơ, mật khẩu, hạng/điểm hiện tại | khachhang | Dùng tendangnhap, matkhau và các cột hồ sơ hiện có |
| Đơn hàng và chi tiết mua | hoadon, chitiethoadon, sanpham | Bỏ phụ thuộc bảng snapshot; không còn snapshot tên/màu/size riêng nếu sản phẩm thay đổi |
| Chương trình và giá khuyến mãi | khuyenmai, khuyenmaisanpham | Dùng quan hệ hiện có |
| Giao dịch QR | giaodichthanhtoanqr | Phải giữ quy ước của partner; hiện một giao dịch/mã hóa đơn |
| Đổi trả và chi tiết đổi trả | phieudoitra, chitietdoitra | Dùng trạng thái/yêu cầu có sẵn; cần khớp luồng partner |
| Giỏ hàng, sản phẩm yêu thích | Không có bảng/cột tương ứng | Đã chốt lưu trên trình duyệt bằng cookie có bảo vệ, 30 ngày; không đồng bộ qua thiết bị |
| Nhiều địa chỉ đã lưu | khachhang chỉ có diachi mặc định | Giữ một địa chỉ mặc định, nhập địa chỉ cho từng hóa đơn |
| Đánh giá sao/nội dung/người đánh giá | Không có bảng/cột tương ứng | Đã chốt tạm ngưng; API trả 410 và giao diện có thông báo |
| Token email khôi phục mật khẩu | Không có bảng/cột tương ứng | Người dùng chọn tạm ngưng khôi phục mật khẩu; đổi mật khẩu khi đang đăng nhập vẫn dùng matkhau hiện có |
| Lịch sử đối soát/hoàn tiền/điểm chi tiết | Chỉ một phần có trong hoadon, phieudoitra, giaodichthanhtoanqr | Không giả định có thể giữ toàn bộ lịch sử đã lưu ở các bảng bổ sung |

## Trạng thái kết nối hiện tại

Theo yêu cầu mới nhất, tập trung kết nối lại 18 bảng sẵn có và để chức năng thiếu nơi lưu sang bước sau.

- Source đọc sản phẩm/khuyến mãi trực tiếp từ bảng và view giá của partner, không phụ thuộc các bảng metadata/snapshot bổ sung.
- Tài khoản dùng khachhang.tendangnhap/matkhau; đăng ký không tạo giỏ trong database.
- Đơn hàng, lịch sử và cập nhật địa chỉ/phương thức dùng hoadon/chitiethoadon. Nội dung SKU lấy từ sanpham hiện tại; không có snapshot tên/màu/size riêng.
- Checkout gửi danh sách SKU/số lượng từ trình duyệt; backend đọc lại giá/tồn kho. Chống trùng bằng dauvantayyeucau có sẵn. Cấp mã HD theo ngày bằng khóa giao dịch và hóa đơn hiện có, không tạo bảng đếm.
- Hủy đơn chưa thanh toán trước khi chuẩn bị giao, chưa có QR; giữ ghi chú gốc và ghi lý do hủy trong ghi chú nghiệp vụ. Đơn đã có thanh toán/QR cần cửa hàng xử lý. Không tạo kho hoàn tiền riêng.
- Đổi trả dùng phieudoitra/chitietdoitra ở trạng thái CHO_DUYET, kiểm tra sở hữu, đã nhận và trả tiền, hạn 15 ngày và số lượng; không thay tồn kho khi gửi yêu cầu.
- Giao dịch QR hiện do partner quản lý; website đọc trạng thái từ giaodichthanhtoanqr. Website hiện mở COD/chuyển khoản theo cấu hình; tạo liên kết và callback cổng thanh toán riêng được để sau, không dùng lại các bảng gateway bổ sung.
- Giỏ/yêu thích lưu cookie có bảo vệ trên trình duyệt; API giỏ/yêu thích trong database tạm ngưng. Đánh giá và khôi phục qua email tạm ngưng. Điểm hiện tại vẫn lấy từ khachhang; không giả lập sổ lịch sử điểm.

Kiểm chứng: 203 bài đạt, 19 bài bỏ qua (cấu trúc bổ sung lịch sử hoặc SQL Server chưa chạy). Có bài tích hợp dựng đúng 18 bảng/cột từ metadata, thử tài khoản, khuyến mãi, tạo đơn/lặp yêu cầu, cập nhật/chặn phiên cũ, hủy giữ ghi chú và yêu cầu đổi trả; không sinh bảng mới. MVC build cùng Razor views đạt. HTTP thật: backend database-connected, 92 sản phẩm, API đánh giá 410; chi tiết và giỏ HTTP 200, cookie giỏ còn khi bỏ session cũ.

Lần triển khai trước đã có 35 bảng. Lượt kết nối này không chạy DDL hoặc xóa dữ liệu trên Supabase: các bảng bổ sung cũ chưa được dọn, nhưng source hiện tại không dùng chúng. Việc dọn cần kiểm kê dependency và dữ liệu trước; không coi chuyển source là bằng chứng database vật lý đã còn 18 bảng.
