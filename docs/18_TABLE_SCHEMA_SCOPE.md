# Giới hạn 18 bảng theo sơ đồ đã chốt

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
| Giỏ hàng, sản phẩm yêu thích | Không có bảng/cột tương ứng | Có thể giữ tạm trên trình duyệt nếu người dùng chấp nhận, không đồng bộ qua thiết bị |
| Nhiều địa chỉ đã lưu | khachhang chỉ có diachi mặc định | Giữ một địa chỉ mặc định, nhập địa chỉ cho từng hóa đơn |
| Đánh giá sao/nội dung/người đánh giá | Không có bảng/cột tương ứng | Chưa thể lưu và đọc đánh giá lâu dài bằng cấu trúc đã chốt |
| Token email khôi phục mật khẩu | Không có bảng/cột tương ứng | Cần chốt việc ngưng chức năng hoặc thay đổi yêu cầu; đổi mật khẩu khi đang đăng nhập vẫn có dữ liệu phù hợp |
| Lịch sử đối soát/hoàn tiền/điểm chi tiết | Chỉ một phần có trong hoadon, phieudoitra, giaodichthanhtoanqr | Không giả định có thể giữ toàn bộ lịch sử đã lưu ở các bảng bổ sung |

## Trạng thái chuyển đổi

- Lần triển khai trước đã tạo 17 bảng bổ sung và đưa public lên 35 bảng. Đó chưa phải cấu trúc đáp ứng giới hạn mới.
- Đã chặn bộ cài cũ tạo lại bảng bổ sung trên database thật.
- Backend/source nghiệp vụ còn phụ thuộc các bảng bổ sung; chưa tuyên bố đã chuyển về 18 bảng.
- Đang chờ chốt cách xử lý chức năng không được thể hiện trong sơ đồ trước khi thay đổi hành vi của website.
- Trước khi loại bỏ bảng bổ sung, phải kiểm kê dữ liệu/dependency, chuyển phần có ánh xạ đúng sang bảng gốc và lưu bản sao phần không có chỗ ánh xạ. Không xóa dây chuyền làm mất dữ liệu bảng gốc.
