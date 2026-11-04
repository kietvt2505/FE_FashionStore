# Đối chiếu 10 mục web và WinForms

Nguồn đối chiếu: WinForms `6d11b10`, giữ nguyên source form. Website dùng 18 bảng hiện có trong `public`; không tạo bảng mới.

| Mục | Xử lý phía website | Giới hạn còn lại |
|---|---|---|
| 1. Mật khẩu | Nhận mật khẩu chữ thường do form cũ ghi; chỉ sau đăng nhập đúng mới thay bằng hash Identity. Không nhận chính chuỗi hash làm mật khẩu. | Không thể giải mã hoặc sửa việc form hiển thị hash nếu không thay nơi form đọc/ghi mật khẩu. Form còn lưu chữ thường cho tới lần đăng nhập web đúng. |
| Quên mật khẩu | Luồng liên kết email, hết hạn, dùng một lần và vô hiệu phiên cũ đã có kiểm thử. | Cửa hàng chưa có email gửi, nên chưa bật gửi thư thật. |
| 2. COD | Lưu `COD`, `Vừa xác nhận đơn`, `CHUA_THANH_TOAN` để form tiếp nhận được. | Form giữ nguyên nên không có bước xác nhận riêng trước trạng thái này. |
| 3. Lịch sử mua | Đọc hóa đơn có đúng `makh`, gồm WEB và POS. Không tự ghép khách theo số điện thoại. | Đơn POS chỉ đọc trên web. |
| 4. Yêu thích | Thêm/bỏ tại chỗ, đồng bộ các nút cùng sản phẩm, chặn bấm lặp; đăng nhập rồi quay lại nguyên URL. | Lưu trên trình duyệt như phạm vi đã chốt. |
| 5. Tồn kho | Giữ/trừ tồn ngay trong transaction tạo đơn; trước thanh toán đã có `datrutonkho=TRUE`. Form không trừ lần hai khi giao thành công. | Cờ tồn lịch sử không xác định cần nhân viên đối chiếu; không đoán và trừ lại hàng cũ. |
| 6. Trạng thái | Các giá trị ghi database theo hợp đồng bên dưới; mã lọc tổng hợp của web không ghi đè trạng thái nghiệp vụ. | QR và xác minh chuyển khoản tiếp tục theo quy trình cửa hàng. |
| 7. Đổi/trả | Lưu `HOAN_TIEN`/`DOI_SAN_PHAM` ở cả phiếu và chi tiết. Chỉ tiếp nhận ở trạng thái chờ; form tính tiền và duyệt. | Migration chỉ sửa mã của yêu cầu WEB đang chờ, giữ nguyên phiếu đã xử lý và tiền/số lượng/lý do/SKU đổi. |
| 8. Thành viên | Hiện bảng điều kiện tại tài khoản, ưu đãi và điểm: Bạc đến 5 triệu, Vàng trên 5 đến 15 triệu, Kim Cương trên 15 triệu. | Dựa tổng chi tiêu đã ghi nhận, không chỉ số điểm hay đơn vừa đặt. |
| 9. Ưu đãi | So sánh số tiền giảm theo hạng và voucher, chọn mức lớn hơn; bằng nhau ưu tiên thành viên. Chỉ lưu mã voucher khi nó được chọn. | Giá sale SKU là đơn giá đầu vào; điều kiện voucher kiểm tra lại trên server. |
| 10. VAT | VAT 8% trên tiền hàng sau giảm, sau đó cộng phí giao hàng. Hiển thị VAT/chiết khấu/phí giao riêng. | Không tính lại VAT hoặc đổi số tiền của hóa đơn đã tồn tại. |

## Giá và hóa đơn

Ví dụ tiền hàng 1.000.000đ, khách Kim Cương, voucher 50.000đ, phí giao 0: ưu đãi thành viên 100.000đ được chọn, VAT 72.000đ, phải trả 972.000đ. Voucher 150.000đ được chọn thay ưu đãi thành viên: VAT 68.000đ, phải trả 918.000đ. Không cộng hai khoản giảm.

Checkout gửi `ExpectedTotal` chỉ làm điều kiện kiểm tra báo giá, không làm giá tính tiền. Server vẫn đọc lại SKU, giá, ưu đãi và VAT. Nếu tổng thay đổi hoặc khách mở trang thanh toán cũ thiếu trường này, website hiển thị tổng mới và yêu cầu xác nhận lại trước khi tạo đơn.

`giatrithuctra` phân bổ theo tổng lũy kế, làm tròn đồng như POS, tổng các dòng bằng tiền hàng thực trả gồm VAT và không gồm phí giao. Đổi/trả dùng số thực trả của hóa đơn gốc.

Yêu cầu gửi lại dùng `dauvantayyeucau` 64 ký tự sẵn có: 32 ký tự định danh request và 32 ký tự băm nội dung. Nhờ vậy, voucher được yêu cầu nhưng không thắng không phải lưu sai vào `makm` hoặc nhét vào ghi chú; gửi lặp vẫn trả lại một hóa đơn, không trừ tồn thêm. Hóa đơn mang fingerprint định dạng cũ vẫn được nhận diện.

## Giá trị nghiệp vụ lưu trong database

| Trường | Giá trị dùng chung với form |
|---|---|
| `hoadon.loaihoadon` COD | `COD` |
| `hoadon.nguondon` | `WEB` |
| `hoadon.trangthaigiaohang` | `Vừa xác nhận đơn` → `Shipper đã lấy hàng` → `Hàng đang được vận chuyển` → `Hàng giao thành công`; hủy: `Đã hủy` |
| `hoadon.trangthaithanhtoan` | `CHUA_THANH_TOAN`, `DA_THANH_TOAN`; không lấy trạng thái giao hàng thay trạng thái thanh toán |
| `phieudoitra.trangthai` | `CHO_DUYET`/`CHO_TIEP_NHAN` → `HOAN_TAT` hoặc `TU_CHOI`, do form xử lý |
| `phieudoitra.hinhthucxuly`, `chitietdoitra.loaigiaodich` | `HOAN_TIEN`, `DOI_SAN_PHAM`; phiếu có nhiều loại có thể là `DOI_VA_TRA` |

API nhận thêm `TRA`/`DOI` để trang cũ còn mở gửi được, nhưng luôn chuyển sang mã chuẩn trước khi ghi. Số tiền khi gửi yêu cầu mới để 0, không tự kết luận đã hoàn tiền. Giao diện phân biệt “Trả hàng, hoàn tiền” là **loại yêu cầu**, với “Đã hoàn tiền” là **kết quả thanh toán**.

## Áp dụng và kiểm chứng

Chạy lại `scripts/Sync-WinForms.ps1 -StartBackend`. Công cụ giữ 18 bảng và kiểm tra dữ liệu ngoài các trường trạng thái/mã nghiệp vụ cho phép không thay đổi. Migration không đổi tiền, VAT, số lượng, lý do hoặc SKU trên đơn/phiếu cũ. Không chạy initializer hoặc sửa source WinForms.

`NativeStoreParityScenarios` kiểm tra các mốc hạng, voucher thấp/bằng/cao hơn, VAT, thời điểm hiệu lực, retry có voucher không áp dụng, hóa đơn định dạng cũ và báo giá hết hiệu lực. Với `PartnerSourceRoot`, bộ kiểm thử gọi nguyên `ValidateInvoicePricingAsync`, `UpdateDeliveryStatusAsync`, `ApproveWebReturnAsync` của form trên database tạm. Có kiểm tra duyệt hoàn tiền, đổi SKU và duyệt lặp không trừ kho/hoàn tiền lần hai.
