# Đơn giao hàng COD và đơn đã thanh toán trước

Ngày 26/10/2026. Nhân viên phụ trách đã đối chiếu từ quá trình chạy thực tế: `NV-001`. Người dùng cung cấp quy trình chuyển khoản do nhân viên kiểm tra biến động số dư rồi xác nhận; chưa chọn cổng thanh toán tự động.

## Hợp đồng dữ liệu với form giao hàng hiện tại

Website ghi chung `public.hoadon` và `public.chitiethoadon`. Cả hai phương thức có `nguondon=WEB`, mã đơn bắt đầu `WEB`, liên kết khách hàng bằng `makh`, chi tiết bằng `masku`. Không tạo lại hóa đơn từ POS khi đã có mã đơn website.

| Trường | COD mới đặt | Chuyển khoản chờ đối soát | Chuyển khoản đã xác nhận |
| --- | --- | --- | --- |
| loaihoadon | COD | WEB_CHO_THANH_TOAN | GIAO_HANG |
| phuongthucthanhtoan | COD | CHUYEN_KHOAN | CHUYEN_KHOAN |
| trangthaithanhtoan | CHUA_THANH_TOAN | CHUA_THANH_TOAN | DA_THANH_TOAN |
| trangthaigiaohang | Vừa xác nhận đơn | Chờ xác nhận thanh toán | Vừa xác nhận đơn |
| tienkhachdua | 0 | 0 | Tổng tiền đã đối soát |
| tiencod | Tổng phải thu | 0 | 0 |
| Nơi hiển thị trên form đã nhận | Danh sách COD | Chưa vào danh sách giao hàng | Danh sách giao hàng đã thanh toán |
| Thời điểm trừ kho và cộng điểm | WinForms xử lý khi giao thành công | Chưa xử lý | Khi xác nhận đã nhận tiền |

Bộ lọc hiện có trong `StoreRepository.QueryShipmentRowsAsync`: COD nhận `COD/WEB_COD`; đã trả tiền nhận `GIAO_HANG/WEB_DA_THANH_TOAN/WEB_THANH_TOAN`. Đơn chờ chuyển khoản không khớp hai bộ lọc, tránh bị giao hoặc thu COD trước khi đối soát. Khi đã xác nhận và chuyển sang GIAO_HANG, form hiện tại nhận được đơn ở lần tải lại dữ liệu kế tiếp; chưa có push/realtime mới.

Form tiếp tục cập nhật tuần tự: Vừa xác nhận đơn → Shipper đã lấy hàng → Hàng đang được vận chuyển → Hàng giao thành công. Website đọc lại trạng thái khi tải trang. Với GIAO_HANG, code WinForms hiện tại không gọi trừ kho/cộng điểm lần nữa khi giao thành công.

## Bật trên website

Hai script tài khoản và đơn hàng trước đó người dùng đã chạy thành công. Với phiên bản code này, chạy thêm **toàn bộ** `database/Supabase-Web-Payments.sql` trong SQL Editor bằng chủ database, trước khi khởi động lại backend. Migration chỉ thêm cột/bảng và hàm; chạy migration không xác nhận thanh toán cho đơn có sẵn, không trừ tồn kho và không cộng điểm. Đơn COD cũ giữ phương thức COD.

Sau đó chạy:

```powershell
.\scripts\Start-SupabaseCatalog.ps1 -EnableOrders -OrderEmployeeId NV-001 -EnableBankTransfer
```

Website hiển thị thêm lựa chọn chuyển khoản. Ba appSettings `BankName`, `BankAccount`, `BankOwner` trong `FashionStore.Web/Web.config` đã được điền từ hàm `buildVietQrUrl` trong bản WinForms được cung cấp (`winform/qlicuahang/js/ung-dung.js`), ngày 27/10/2026. Thông tin này trùng với ảnh QR TPBank người dùng cung cấp sau đó: chủ tài khoản VUONG ANH VU, số tài khoản 00006027155.

Trang chi tiết đơn hiển thị ảnh QR do người dùng cung cấp, số tiền và nội dung chuyển khoản là mã đơn. Ảnh gốc được lưu trong `Content/images/payments/tpbank-transfer-qr.png`; giao diện chỉ hiển thị vùng QR, có thêm liên kết tải ảnh đầy đủ. QR này tĩnh, không tự điền số tiền hoặc mã đơn. Khách cần nhập hai thông tin đúng như hướng dẫn bên cạnh. Website chưa tự đối soát với ngân hàng.

Ba cấu hình `BankQrImage`, `BankQrAccount`, `BankQrBank` gắn ảnh QR với tài khoản nhận tiền. Nếu ngân hàng/số tài khoản chính thay đổi mà chưa cập nhật QR thì ảnh sẽ bị ẩn để tránh chuyển nhầm. Khi thay tài khoản cửa hàng, cập nhật cả thông tin nhận tiền và ảnh/cấu hình QR tương ứng. QR không hiển thị cho đơn đã thanh toán hoặc đã hủy. Việc quét hoặc tải ảnh không cập nhật trạng thái thanh toán.

## Xác nhận tiền đã nhận

Chưa có nút xác nhận tiền mới trong WinForms. Ở bước tích hợp này, người có quyền database đối soát rồi gọi hàm trong SQL Editor. Chỉ gọi sau khi đã kiểm tra tiền thực nhận và mã giao dịch ngân hàng; khách bấm đặt hàng hoặc gửi ảnh chuyển khoản không tự xác nhận thanh toán.

Ví dụ mẫu, phải thay mã đơn, số tiền và mã giao dịch bằng kết quả đối soát thực tế:

```sql
SELECT webstore.confirm_bank_payment(
    'WEB_MA_DON_THUC_TE',
    'NV-001',
    398000,
    'MA_GIAO_DICH_NGAN_HANG_THUC_TE'
);
```

Kết quả `CONFIRMED`: đã xác nhận, trừ kho/cộng điểm và chuyển đơn sang nhóm giao hàng đã trả tiền trong cùng một giao dịch. `ALREADY_CONFIRMED`: yêu cầu trùng đã được xử lý, không ghi/trừ/cộng lần nữa. Mỗi mã giao dịch chỉ được gắn với một đơn; số tiền phải khớp chính xác tổng phải trả. Chưa hỗ trợ trả thiếu, trả dư hoặc chia nhiều giao dịch cho một đơn.

Nếu báo thiếu hàng hoặc sai lệch hai cột số lượng, giao dịch database hoàn tác và đơn vẫn chờ xác nhận; việc khách đã chuyển tiền ngoài ngân hàng không tự hoàn tác. Nhân viên phải xử lý bổ sung hàng hoặc hoàn tiền bên ngoài theo quy trình cửa hàng. Hệ thống chưa tự động hoàn tiền.

Hàm dùng SECURITY INVOKER, không cấp EXECUTE cho PUBLIC/anon/authenticated; bảng đối soát bật RLS. Không có endpoint khách hàng tự xác nhận đã trả tiền. Khi partner bổ sung nút “Xác nhận đã nhận tiền” trên form, backend Windows đã xác thực/phân quyền sẽ gọi chính hàm này bằng tham số hóa; không chỉ UPDATE trangthaithanhtoan vì sẽ bỏ qua kiểm tra tồn và đối soát.

## Kiểm thử và giới hạn

Đã chạy trên PostgreSQL riêng: hai phương thức tạo đúng loại/trạng thái, quyền xem theo tài khoản, retry, số tiền sai, nhân viên không hợp lệ, thiếu hàng, xác nhận hai lần đồng thời, dùng lại mã giao dịch cho đơn khác và hoàn tác; tồn/điểm chỉ ghi một lần ở bước xác nhận. Sau xác nhận, dữ liệu khớp bộ lọc giao hàng của form đã khảo sát. Kiểm thử mô phỏng cập nhật trạng thái bằng SQL; chưa chạy ứng dụng Windows thật với đơn mới này.

80 kiểm thử thành công, 15 SQL Server bỏ qua; MVC/Razor biên dịch thành công. Chưa áp dụng migration thanh toán hoặc xác nhận tiền vào Supabase chung trong phiên triển khai này.

Phí giao hiện 0, chưa mở đổi điểm thành tiền. Website chỉ tự giảm theo hạng thành viên, không cộng thêm voucher, theo `SUPABASE_MEMBERSHIP_PRICING.md`; cần chạy migration riêng trước khi dùng ưu đãi thành viên. Số tiền đối soát là tổng sau giảm đã lưu trong hóa đơn, không áp hạng lần hai. COD và chuyển khoản chờ chưa có giữ chỗ tồn kho; vẫn theo dõi hạn chế tồn đồng thời và bản vá WinForms đã chuẩn bị trong `WinForms-Order-Concurrency.patch`. Quy trình người dùng mô tả COD “tạm giữ” chưa được hiện thực thành cơ chế giữ kho; không coi kiểm tra tồn lúc đặt là giữ hàng.
