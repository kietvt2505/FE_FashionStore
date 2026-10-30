# Hoàn thiện các trang tài khoản trên Supabase

## Bật trên database dùng chung

1. Giữ nguyên các migration tài khoản/giỏ hàng và đơn hàng đã chạy thành công. Nếu dùng chuyển khoản, giữ cả migration thanh toán.
2. Mở `database/Supabase-Customer-Features.sql`, sao chép toàn bộ vào Supabase SQL Editor rồi Run. File tạo năm bảng riêng trong schema `webstore`; không sửa tồn kho, số dư điểm, giá bán hoặc hoàn tiền. Có thể chạy lại file này.
3. Khởi động lại backend với lệnh đang dùng:

```powershell
.\scripts\Start-SupabaseCatalog.ps1 -EnableOrders -OrderEmployeeId "NV-001" -EnableBankTransfer
```

4. Đăng nhập website và mở lần lượt Hồ sơ, Địa chỉ, Điểm & hạng, Ưu đãi, Yêu thích, Đổi trả, Đổi mật khẩu. Nếu chưa thấy thay đổi giao diện, tải lại bằng Ctrl+F5; nếu thay đổi backend chưa có hiệu lực, dừng backend cũ rồi chạy lại.

Kiểm tra sau khi chạy SQL:

```sql
SELECT to_regclass('webstore.address') AS dia_chi,
       to_regclass('webstore.wishlist') AS yeu_thich,
       to_regclass('webstore.product_review') AS danh_gia,
       to_regclass('webstore.return_request') AS yeu_cau_doi_tra,
       to_regclass('webstore.password_reset') AS khoi_phuc_mat_khau;
```

Cả năm cột phải có tên bảng, không phải `NULL`. Các bảng bật RLS và thu hồi quyền truy cập trực tiếp của trình duyệt. Backend kết nối bằng tài khoản PostgreSQL phía máy chủ; mọi truy vấn dữ liệu riêng đều giới hạn theo khách hàng đang đăng nhập. Không đưa chuỗi kết nối vào mã giao diện.

## Những chức năng đã có

- Sổ địa chỉ: thêm/sửa/xóa và chọn địa chỉ đã lưu khi thanh toán, tối đa 20 địa chỉ mỗi khách hàng. Sửa địa chỉ không tạo bản ghi mới và không thay đổi thông tin khách hàng khác.
- Yêu thích: lưu/bỏ lưu sản phẩm, mỗi sản phẩm xuất hiện một lần dù có nhiều size/màu.
- Ưu đãi: đọc chương trình đang hoạt động từ `public.khuyenmai`, bao gồm ngày kết thúc. Chạy thêm `database/Supabase-Checkout-Promotions.sql` để bật áp mã lúc thanh toán; hướng dẫn trong `CUSTOMER_FUNCTIONAL_READINESS.md`. Trang sản phẩm khuyến mãi vẫn sử dụng mức giá catalog đã cấu hình trước đó.
- Điểm & hạng: số dư/hạng dùng hồ sơ khách hàng chung. Lịch sử hiện liệt kê các lần xác nhận chuyển khoản website trong `webstore.payment_confirmation`; đây không phải sổ điểm đầy đủ của POS/COD và không tự cộng điểm lần nữa.
- Tiến trình đơn: hiển thị mốc tạo đơn và mốc giao thành công có thời gian thực từ database; trạng thái hiện tại tiếp tục lấy từ hóa đơn mà WinForms cập nhật. Không tạo thời gian giả cho các bước trung gian.
- Đánh giá: chỉ người mua sản phẩm trong đơn đã giao thành công và đã thanh toán được gửi một đánh giá cho sản phẩm/đơn. Có thể bấm “Viết đánh giá” ngay từ chi tiết đơn, mã sản phẩm và mã đơn được điền sẵn. Đánh giá mới chờ duyệt; công khai tên chung “Khách hàng”.
- Đổi/trả: tiếp nhận yêu cầu theo đơn, SKU, số lượng và lý do. Chỉ nhận đơn đã giao và thanh toán, có mốc giao xác thực trong 15 ngày. Kiểm tra số đã yêu cầu và số đã đổi/trả qua WinForms; chống gửi trùng/vượt số lượng. Chưa tự động hoàn tiền, giữ hàng đổi, cộng/trừ tồn hay điều chỉnh điểm.
- Quên mật khẩu: lưu mã băm của liên kết dùng một lần, hết hạn sau 20 phút. Đặt lại mật khẩu làm hết hiệu lực phiên đăng nhập cũ. Gửi email cần cấu hình ở phần sau.

## Nhân viên tiếp nhận yêu cầu và duyệt đánh giá

**WinForms hiện chưa có màn hình đọc `webstore.return_request` hoặc duyệt `webstore.product_review`.** Các chức năng ghi nhận này đã có trên website, nhưng không tự xuất hiện trong danh sách đổi/trả cũ của WinForms. Không chèn một phiếu hoàn tất vào `public.phieudoitra` chỉ vì khách vừa gửi yêu cầu.

Người có quyền database có thể đọc danh sách chờ từ SQL Editor:

```sql
SELECT sophieudt, sohd, makh, masku, quantity, kind, newsku, reason, createdat, status
FROM webstore.return_request
WHERE status IN ('CHO_DUYET','DA_DUYET')
ORDER BY createdat;

SELECT id, masp, sohd, stars, body, createdat
FROM webstore.product_review
WHERE status='CHO_DUYET'
ORDER BY createdat;
```

Sau khi kiểm tra điều kiện thực tế, nhân viên cập nhật trạng thái yêu cầu sang `DA_DUYET` hoặc `TU_CHOI` và ghi `rejectionreason` khi từ chối. Việc hoàn tiền/đổi hàng thực hiện theo nghiệp vụ WinForms đang dùng, sau đó cập nhật yêu cầu website sang `HOAN_TAT` và lưu mã phiếu thật vào `handled_return_id`. Phải hoàn tất bước liên kết này khi xử lý phiếu để tránh tính đồng thời cả yêu cầu chờ và phiếu đổi trả đã hoàn tất. Khi chưa có màn hình tích hợp, cần người có quyền database phối hợp bước này.

Đánh giá chỉ xuất hiện khi nhân viên chuyển `status` sang `HIEN_THI`; `TU_CHOI` sẽ không hiển thị. Chưa mở API quản trị công khai cho các thao tác duyệt.

## Cấu hình email khôi phục mật khẩu

Backend đọc các cấu hình sau (biến môi trường dùng dấu `__`):

| Cấu hình | Nội dung |
|---|---|
| `Recovery__SmtpHost` | Máy chủ email của cửa hàng |
| `Recovery__SmtpPort` | Cổng SMTP, mặc định 587 với TLS |
| `Recovery__From` | Địa chỉ gửi email |
| `Recovery__SmtpUser` | Tài khoản SMTP nếu nhà cung cấp yêu cầu |
| `Recovery__SmtpPassword` | Mật khẩu ứng dụng SMTP, chỉ lưu phía máy chủ |
| `Recovery__ResetUrl` | URL HTTPS của trang `/Account/Reset` trên website thật |

Khi thiếu SMTP hoặc URL HTTPS, trang Quên mật khẩu sẽ thông báo chưa được cấu hình, không báo gửi email thành công. Khi cấu hình đầy đủ, API trả cùng một phản hồi cho địa chỉ tồn tại/không tồn tại và không trả liên kết/token trong HTTP hoặc log. Chưa kiểm thử gửi email thật vì nhóm chưa cung cấp cấu hình này.

## Kiểm thử đã thực hiện

Đã chạy kiểm thử API với PostgreSQL riêng trên máy, bằng dữ liệu giả: chạy migration hai lần, giới hạn địa chỉ khi gửi đồng thời, tách dữ liệu hai tài khoản, ưu đãi theo ngày, duyệt/ẩn đánh giá, điều kiện đổi trả, yêu cầu trùng, đối chiếu số lượng đã trả qua bảng WinForms, khôi phục mật khẩu hết hạn/dùng lại và vô hiệu phiên cũ. Chưa áp file SQL này lên Supabase dùng chung trong lượt sửa code.
