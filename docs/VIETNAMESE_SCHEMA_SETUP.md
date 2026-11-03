# Website dùng bảng tiếng Việt và cấu trúc partner hiện tại

Ngày: 03/11/2026. Project: Khoa Luan Cu Nhan (`oagcfzcslkkiokbmkrlg`), database `postgres`, schema `public`.

## Cấu trúc đã đọc trực tiếp

Metadata hiện tại được lưu trong `database/schema-reference/Supabase_public_metadata_20261103.json`, không có nội dung bản ghi hoặc thông tin kết nối.

- 18 bảng nghiệp vụ, view `sanphamkhuyenmai_hientai`, hai sequence mã đơn/payOS cũ vẫn tồn tại. Các hàm website cũ cũng còn nhưng tham chiếu bảng đã xóa; bộ cài cập nhật lại phần thân hàm.
- `khachhang` đã có `tendangnhap varchar(50)`, `matkhau varchar(200)`, `nhankhuyenmai`. Source dùng trực tiếp các cột này, không tạo lại bảng tài khoản riêng. Mật khẩu mới/đổi mật khẩu dùng ASP.NET Identity hash; không đổi hay đặt lại giá trị mật khẩu đã có khi cài. Giá trị không phải định dạng hash được hỗ trợ không được coi là mật khẩu hợp lệ bằng cách so sánh trực tiếp.
- `sanpham` đã có `danhmuc`, `mota`, `hinhanh`, `danhsachhinhanh`. Website dùng danh mục hiện tại và quy tắc loại sản phẩm; ảnh/mô tả lấy từ bảng này. Bộ cài chính không tạo thêm bảng metadata, ảnh hoặc bảng sửa giá sản phẩm.
- Giá khuyến mãi lấy từ view partner hiện tại, qua adapter chỉ đọc `giabanthuctesanpham`; sản phẩm không nằm trong chương trình vẫn có giá thường.
- Giữ nguyên `giaodichthanhtoanqr`. Dữ liệu này là giao dịch QR một dòng/mã hóa đơn; website chặn tạo giao dịch mới/đổi phương thức nếu đơn đã có QR ở bảng này. Các bảng yêu cầu/biên nhận website bổ sung phục vụ nhiều giao dịch và kết quả xác thực VNPay/Visa/ZaloPay/payOS; không tự ghi đè hoặc xác nhận thanh toán bản ghi QR của partner.

## Các bảng bổ sung

Tên theo bảng sẵn có: tiếng Việt không dấu, chữ thường, viết liền. Quan hệ sử dụng `khachhang.makh`, `sanpham.masku` và cùng mã hóa đơn `hoadon.sohd`.

| Bảng | Mục đích |
|---|---|
| giohang, chitietgiohang | Giỏ khách theo tài khoản và SKU |
| diachikhachhang | Nhiều địa chỉ nhận hàng đã lưu |
| sanphamyeuthich | Danh sách yêu thích |
| danhgiasanpham | Đánh giá xác minh đã nhận hàng, mỗi sản phẩm/đơn một lần |
| yeucaudoitra | Yêu cầu chờ xử lý, phân biệt phiếu đổi/trả đã hoàn tất của partner |
| yeucaukhoiphucmatkhau | Token băm, thời hạn và trạng thái đã dùng |
| thongtindonhang, chitietdonhanglucmua | Dữ liệu web và thông tin lúc mua, liên kết hóa đơn hiện tại |
| uudaidonhang | Các khoản giảm/hạng được chốt lúc mua; giamgiatruocdo giữ phần giảm lịch sử chưa có nguồn chi tiết |
| lichsuhuydonhang | Lý do hủy và dữ liệu bảo toàn khi hủy |
| yeucauhoantien | Thông tin/yêu cầu/đối soát hoàn tiền thủ công |
| xacnhanthanhtoanchuyenkhoan | Nhân viên xác minh chuyển khoản |
| yeucauthanhtoantructuyen, biennhanthanhtoantructuyen | Tạo giao dịch và chống ghi nhận kết quả online lặp |
| lichsugopgiohang | Chống cộng giỏ khách nhiều lần khi thử lại |
| sothutudonhangtheongay | Cấp mã đơn dùng chung giữa các instance |

Tổng cộng 17 bảng. Tên tương ứng với định nghĩa cũ được liệt kê trong `database/schema-reference/Website_Table_Names_Vietnamese.json`. Script thành phần cũ cũng đã chuyển sang các tên mới; lịch sử chuyển schema/rollback ngày 02/11 được giữ nguyên để đối chiếu.

## Áp dụng và chạy

Bộ SQL đã gộp ở `database/Supabase-Website-TenViet.sql`. Có thể dựng lại từ script thành phần bằng `scripts/Build-VietnameseSchema.ps1`.

Cách áp dụng có kiểm tra dữ liệu gốc trước khi commit:

```powershell
.\scripts\Apply-VietnameseSchema.ps1 -StartBackend -EnableBankTransfer -OrderEmployeeId "NV-001"
```

Nhập chuỗi PostgreSQL hiện dùng vào lời nhắc nhập kín, không ghi mật khẩu trong lệnh/source. Bộ áp dụng xác minh đúng project và cấu trúc 18 bảng đã kiểm kê, khóa ngắn hạn trong transaction, tạo bảng/hàm/view bổ sung, so sánh checksum toàn bộ dữ liệu bảng gốc rồi mới commit. Nếu cấu trúc đã đổi hoặc dữ liệu gốc bị thay đổi bởi bộ cài, transaction bị rollback. Sequence có thể đã tăng dù transaction rollback; mã không được giảm hoặc tái sử dụng.

Chức năng được khôi phục trong cùng schema public. Bộ cài không reset/seed lại sản phẩm, không xóa hóa đơn, không đổi số tiền/tồn kho/điểm của bảng gốc và không tạo database mới. Bảng website có RLS và thu hồi quyền trực tiếp từ PUBLIC/anon/authenticated; backend tiếp tục dùng kết nối PostgreSQL phía máy chủ.

Những hóa đơn `nguondon='WEB'` còn tồn tại được nối vào dữ liệu website bằng cùng mã đơn. Giá/số lượng lấy từ chi tiết hóa đơn; nếu snapshot cũ đã bị xóa thì tên/màu/size chỉ có thể lấy từ sản phẩm hiện tại, không thể phục hồi bản gốc lúc mua. Các đánh giá, yêu thích, giỏ hoặc mật khẩu chỉ tồn tại trong bảng đã xóa không được khôi phục chỉ bằng tạo bảng.

Nếu chỉ chạy backend sau khi đã cài:

```powershell
.\scripts\Start-SupabaseCatalog.ps1 -EnableOrders -OrderEmployeeId "NV-001" -EnableBankTransfer
```

## Kiểm thử

- 206 kiểm thử đạt trên PostgreSQL localhost; 15 kiểm thử SQL Server được bỏ qua do không có SQL Server thử nghiệm.
- Dựng lại cấu trúc 18 bảng/cột/khóa/view từ metadata thật, tạo dữ liệu giả, chạy bộ cài hai lần; checksum dữ liệu gốc giữ nguyên, có 35 bảng public và không tạo lại các bảng tiếng Anh cũ.
- Dùng tài khoản ngay trong khachhang để đăng nhập; đăng ký, sửa hồ sơ, đổi mật khẩu và thu hồi phiên cũ đã được kiểm tra trên cấu trúc hiện tại. Khách tại quầy chưa có thông tin đăng nhập không phát sinh token khôi phục mật khẩu.
- Giá khuyến mãi, phân loại Nam, ảnh JSON, mô tả native, giỏ, tạo đơn, hủy và tồn kho đã được kiểm tra.
- Kiểm thử trực tiếp bộ áp dụng: phát hiện SQL làm thay đổi bảng gốc và rollback toàn transaction.
- MVC build với MvcBuildViews=true đã đạt. Không tạo tài khoản, đơn hay giao dịch thử trên Supabase thật.
- Kết quả áp dụng thật được ghi riêng tại `evidence/vietnamese-schema-live-result.json` sau khi thành công; không coi kiểm thử localhost là bằng chứng đã áp dụng lên Supabase.
