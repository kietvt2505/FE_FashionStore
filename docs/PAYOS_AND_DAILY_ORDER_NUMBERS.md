# QR payOS và mã đơn theo ngày

## Thiết lập database

1. Chạy `database/Supabase-Daily-Order-Numbers.sql`: kết quả `co_ma_hd_theo_ngay=true`.
2. Nếu chưa chạy, chạy `database/Supabase-Online-Payments.sql` trước.
3. Chạy `database/Supabase-PayOS.sql`: kết quả `co_payos=true`.
4. Khởi động lại backend bằng lệnh quen thuộc có `-EnableOrders -OrderEmployeeId "NV-001" -EnableBankTransfer`.

Chỉ các đơn mới đổi sang dạng **HD29102026-1234**, ngày theo Việt Nam, số cuối ngẫu nhiên từ **0001–9999**, giữ mã đã dùng kể cả đơn hủy. Tối đa 9.999 mã/ngày. Khi hết số, hệ thống báo hết mã, không tăng số chữ số hay dùng trùng mã. Database kiểm tra cả hóa đơn WinForms và snapshot; nhiều website dùng chung bộ cấp mã. Mã đơn cũ vẫn giữ nguyên. Khi chưa chạy migration ngày, backend tiếp tục dùng mã WEB cũ để không làm gián đoạn đặt hàng.

payOS cần mã giao dịch số nguyên nên có bộ đếm riêng, ánh xạ về mã hóa đơn HD. Không dùng 4 số cuối làm mã payOS vì sẽ trùng qua các ngày.

## Khóa và QR

Các khóa thuộc backend, lưu bằng .NET User Secrets của project `FashionStore-Backend-Development` và cấu hình local `FashionStore.Backend/appsettings.Development.json` đã được Git bỏ qua. Bản local giúp backend chạy từ PowerShell/IDE nạp được cùng cấu hình. Không gửi lại khóa qua chat hoặc thêm vào `Web.config`/JavaScript; không đưa file Development chứa khóa vào file ZIP chia sẻ. Script hỗ trợ nhập ẩn và cập nhật:

```powershell
.\scripts\Configure-PayOS.ps1
# Đổi khóa khi cần:
.\scripts\Configure-PayOS.ps1 -RotateKeys
```

Tài khoản được đối chiếu với ảnh nhóm cung cấp: **MB Bank**, **0924020138**, chủ tài khoản **VUONG ANH VU**. Phản hồi tạo QR phải có chữ ký đúng, số tiền/mã giao dịch đúng, đúng BIN MB và số tài khoản đã cấu hình. Nếu kênh payOS liên kết tài khoản khác hoặc tài khoản ảo khác, backend từ chối mở QR; đối chiếu lại kênh payOS trước khi sửa cấu hình.

Phương thức **Quét mã QR ngân hàng (payOS)** mở trang QR của payOS với số tiền của đơn. Phương thức chuyển khoản thủ công cũng dùng ảnh MB mới, vẫn cần nhân viên đối soát. Không coi việc khách quét ảnh QR tĩnh là bằng chứng đã thanh toán.

## Chạy local và nhận kết quả

Trong chế độ local, URL quay lại mặc định là API `http://127.0.0.1:5000` và website `http://localhost:58472`. Sau khi trả tiền, mở chi tiết đơn và bấm **Kiểm tra kết quả thanh toán**; backend truy vấn payOS, xác thực chữ ký, trạng thái PAID, số tiền đã nhận, mã link và mã giao dịch. Return/cancel URL chỉ điều hướng, không xác nhận tiền hoặc hủy hóa đơn.

Chưa có HTTPS công khai thì payOS không gọi webhook vào máy local. Đơn sẽ chưa tự đổi trạng thái khi khách đóng trang; có thể đối soát bằng nút kiểm tra. Đây là payOS thật, chế độ local không tạo môi trường ngân hàng giả hay biến tiền thật thành tiền thử.

Khi đã có HTTPS:

```powershell
.\scripts\Configure-PayOS.ps1 -PublicApiBaseUrl "https://API-CUA-NHOM" -StorefrontBaseUrl "https://WEB-CUA-NHOM"
```

Khởi động lại backend; cấu hình webhook trên dashboard payOS là `https://API-CUA-NHOM/api/payments/payos/webhook`. Không tự đăng ký URL khi nhóm chưa cung cấp. payOS gửi webhook thử có chữ ký: backend chấp nhận mẫu hợp lệ mà không tạo hóa đơn.

Webhook hợp lệ kích hoạt truy vấn lại máy chủ payOS để kiểm tra tổng tiền, tránh đánh dấu đủ tiền chỉ từ một lần chuyển thiếu. Thông báo lặp chỉ trừ kho và cộng điểm một lần. Tiền nhận sau khi đơn đã hủy hoặc hết hàng được giữ ở REVIEW để cửa hàng đối soát/hoàn tiền; chưa hoàn tiền tự động.

Nguồn giao thức: [API payOS](https://payos.vn/docs/api/), [chữ ký payment-requests](https://payos.vn/docs/tich-hop-webhook/kiem-tra-du-lieu-voi-signature/), [ví dụ chính thức chạy localhost](https://payos.vn/docs/checkout/quick-start-payos-hosted-page/).

## Kết quả kiểm tra ngày 29/10/2026

- 118 kiểm thử đạt, 15 kiểm thử SQL Server cũ được bỏ qua vì không cấu hình SQL Server; MVC/Razor biên dịch thành công.
- Database thử nghiệm riêng: cấp mã đồng thời không trùng, xử lý mã đã dùng từ WinForms, hết 9.999 số thì từ chối cấp tiếp; retry cùng yêu cầu giữ nguyên mã đơn.
- payOS giả lập: chữ ký sai/số tiền bị sửa/tiền tệ sai bị từ chối, tài khoản nhận sai bị chặn; webhook lặp chỉ trừ kho/cộng điểm một lần.
- Sau khi người dùng cho phép, tạo đúng một link payOS thật 1.000đ cho đơn local **HD29102026-7849**. Phản hồi có chữ ký hợp lệ, đúng tài khoản MB. Link đã được hủy ngay, payOS xác nhận **CANCELLED**, **amountPaid=0**. Không có chuyển tiền và không tạo đơn kiểm tra trên Supabase chung.
- Người dùng xác nhận đã chạy ba migration thành công, hai cờ `co_payos` và `co_ma_hd_theo_ngay` đều true.
- Đã đối chiếu backend thật `127.0.0.1:5000`: database-connected, phương thức PAYOS có `enabled=true`. Website thật `localhost:58472` và ảnh QR MB đều trả HTTP 200.
- Chưa có HTTPS công khai; chưa đăng ký hoặc thử webhook công khai. Chưa thử nhận tiền thật; kiểm thử ghi nhận thanh toán dùng dữ liệu giả và PostgreSQL riêng.
