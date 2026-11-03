# Bổ sung cột tiếng Việt, giữ 18 bảng gốc

Người dùng đã cho phép bổ sung cột phù hợp chức năng vào bảng hiện có. Không tạo bảng mới. Tên cột dùng tiếng Việt không dấu, chữ thường, viết liền như tên bảng của partner.

## Danh sách 19 cột

| Bảng | Cột | Kiểu dữ liệu |
|---|---|---|
| khachhang | makhoiphucbam | varchar(64) |
| khachhang | hethanmakhoiphuc | timestamptz |
| khachhang | phienmatkhaukhoiphuc | varchar(64) |
| chitiethoadon | masplucmua | varchar(20) |
| chitiethoadon | tensanphamlucmua | varchar(200) |
| chitiethoadon | kichcolucmua | varchar(30) |
| chitiethoadon | mausaclucmua | varchar(60) |
| chitiethoadon | hinhanhlucmua | text |
| chitiethoadon | sosao | smallint |
| chitiethoadon | noidungdanhgia | varchar(2000) |
| chitiethoadon | ngaydanhgia | timestamptz |
| chitiethoadon | trangthaidanhgia | varchar(20) |
| hoadon | lydohuy | varchar(500) |
| hoadon | ngayhuy | timestamptz |
| phieudoitra | tennganhanghoantien | varchar(100) |
| phieudoitra | sotaikhoanhoantien | varchar(34) |
| phieudoitra | chutaikhoanhoantien | varchar(150) |
| phieudoitra | mathamchieuhoantien | varchar(100) |
| phieudoitra | ngayhoantien | timestamptz |

Các cột mới cho phép NULL để giữ tương thích với dòng cũ và form hiện tại. Giữ nguyên cột, số tiền, tồn kho, điểm và mật khẩu cũ. Cập nhật thuộc tính của bốn lớp tương ứng trong sơ đồ lớp, không phát sinh lớp bảng mới.

## Cách sử dụng

- **Đánh giá:** dùng chi tiết hóa đơn và khách hàng của hóa đơn. Vẫn yêu cầu đơn WEB thuộc khách, nhận hàng thành công và đã thanh toán. Mỗi sản phẩm/đơn chỉ được đánh giá một lần, kể cả mua nhiều SKU của cùng sản phẩm. Khóa hóa đơn khi gửi và có chỉ mục duy nhất để chống gửi đồng thời. Đánh giá hiện/viết dưới chi tiết sản phẩm như trước. Không công khai mã đơn hay SKU từ API đánh giá công khai.
- **Thông tin lúc mua:** trigger ghi mã/tên/size/màu/ảnh khi thêm chi tiết hóa đơn, áp dụng cả khi form thêm đơn. Sửa danh mục về sau không làm thay thông tin đã lưu. Với hóa đơn cũ chỉ sao được thông tin danh mục hiện tại, không khôi phục được lịch sử đã bị xóa. Chạy lại bộ cài không ghi đè snapshot đã có.
- **Hủy đơn:** ghi riêng lý do và thời điểm hủy; không còn ghép vào ghi chú giao hàng. Không tự đoán thời điểm hủy của đơn lịch sử. Các điều kiện hủy chưa thanh toán/chưa giao vẫn giữ.
- **Khôi phục mật khẩu:** chỉ lưu SHA-256 của mã ngẫu nhiên và dấu băm phiên mật khẩu/email, hạn 20 phút; mã mới thay mã cũ, dùng một lần. Đổi email/mật khẩu khiến mã cũ mất hiệu lực. Reset xóa ba trường và làm phiên đăng nhập cũ mất hiệu lực. Backend cần SMTP, địa chỉ From và ResetUrl HTTPS hợp lệ để gửi email; chưa cấu hình thì trả thông báo 503, không lộ mã trong API/log.
- **Hoàn tiền:** tài khoản và tham chiếu nằm trên phiếu đổi trả, dùng lại số tiền/trạng thái/nhân viên hiện có. Khách chỉ bổ sung tài khoản cho phiếu đang chờ thuộc đơn của mình. API che số tài khoản, không tự xác nhận đã chuyển tiền. Form/nhân viên cần ghi ngày và tham chiếu sau khi đối soát thực tế; không tự điền cho dữ liệu lịch sử.
- **Không đổi phạm vi khác:** giỏ/yêu thích vẫn lưu trình duyệt; nhiều địa chỉ, sổ lịch sử điểm và sự kiện đơn chi tiết tiếp tục để sau. Cổng QR do luồng partner quản lý.

## Áp dụng

SQL: `database/Supabase-BoSungCot-ChucNang.sql`.

```powershell
.\scripts\Apply-Native18Columns.ps1 -StartBackend -OrderEmployeeId "NV-001"
```

Nhập chuỗi PostgreSQL vào lời nhắc kín. Bộ áp dụng khóa giao dịch, đối chiếu 18 bảng/cột gốc với metadata, kiểm tra kiểu của 19 cột cho phép, so sánh checksum dữ liệu cột gốc và danh sách bảng trước/sau rồi mới commit. Sai cấu trúc, SQL đổi dữ liệu cũ hoặc sinh/xóa bảng sẽ bị rollback. Chỉ sau khi áp dụng thành công mới chạy lại backend dùng cột mới. Bộ cài 17 bảng bổ sung cũ vẫn bị chặn.

## Kết quả đã kiểm tra

Đã áp dụng trên project Supabase Khoa Luan Cu Nhan: **18 bảng trước/sau**, **19 cột trên 4 bảng**, dữ liệu cột gốc giữ nguyên. Báo cáo riêng: `evidence/native18-columns-live-result.json`; không chứa nội dung bản ghi hoặc thông tin kết nối. Metadata baseline `Supabase_public_metadata_20261103.json` được giữ nguyên để tiếp tục đối chiếu các cột gốc.

203 kiểm thử đạt, 19 kiểm thử SQL Server/cấu trúc cũ bỏ qua. Bài tích hợp 18 bảng kiểm tra cài hai lần, rollback SQL sửa giá cũ, snapshot, mua/cập nhật/hủy, đánh giá đúng điều kiện và chống trùng nhiều SKU, quyền sở hữu, hoàn tiền che số tài khoản, token hết hạn/đổi email/dùng đồng thời/chống dùng lại và thu hồi phiên cũ. Thử khôi phục dùng SMTP localhost không hoạt động, không gửi email thật.

MVC/Razor build đạt. Sau áp dụng thật: backend database-connected; API đánh giá trả 200; sản phẩm, phần đánh giá dưới chi tiết, trang quên mật khẩu và giỏ trả HTTP 200. Không tạo tài khoản/đơn/đánh giá hay gửi email thử trên Supabase thật. Chưa kiểm tra trực quan qua browser vì Local browser use bị tắt.
