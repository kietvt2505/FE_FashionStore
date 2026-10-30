# Khuyến mãi sản phẩm và thanh toán trực tuyến

## Kết nối khuyến mãi WinForms

Chạy `database/Supabase-Product-Promotions.sql` sau `Supabase-Storefront-Enhancements.sql`. File tạo view chỉ đọc `webstore.sku_price`, không sửa giá trong `public.sanpham`.

Nguồn dữ liệu đã đối chiếu với ảnh nhóm cung cấp:

- `public.khuyenmaisanpham`: `makm`, `masku`, `phantramgiam`.
- `public.khuyenmai`: mã chương trình, `trangthai`, `ngaybatdau`, `ngayketthuc`, `giatridonhangtoithieu`.

Giảm theo từng SKU, lấy mức cao nhất trong các chương trình đang bật, còn hạn theo ngày Việt Nam, phần trăm lớn hơn 0 và nhỏ hơn 100. Không cộng dồn các chương trình. Chương trình có điều kiện giá trị đơn tối thiểu không được quảng cáo như giảm giá vô điều kiện từng sản phẩm. Partner cần để mức tối thiểu bằng 0 cho khuyến mãi SKU này. Giá làm tròn đến 1 đồng. Nếu đã dùng hàm giảm giá thử nghiệm cũ, ưu tiên giá gốc đã lưu khi giá sản phẩm vẫn khớp với lần giảm cũ để tránh giảm hai lần.

Trang chủ, danh sách, bộ lọc giá, chi tiết, giỏ hàng và tạo đơn dùng chung view. Thẻ khuyến mãi chọn SKU được giảm; SKU khác của cùng sản phẩm giữ giá riêng. Khi partner đổi phần trăm/tắt chương trình, lần tải tiếp theo nhận giá mới; hóa đơn đã đặt giữ nguyên giá chốt. Ưu đãi theo hạng tiếp tục tính trên tổng tiền hàng sau giảm giá SKU, đúng luồng hiện có.

Kiểm tra:

```sql
SELECT p.masku,p.tensanpham,s.base_price AS gia_goc,s.sale_price AS gia_ban,s.native_percent AS giam_phan_tram
FROM webstore.sku_price s JOIN public.sanpham p USING(masku)
WHERE s.native_percent IS NOT NULL ORDER BY p.masku;
```

Nếu kết quả trống, kiểm tra mã `makm` phải tồn tại ở cả hai bảng, SKU khớp, trạng thái bật, ngày áp dụng và giá trị đơn tối thiểu. Không tạo khuyến mãi giả để lấp trang chủ.

Đã kiểm tra trên backend Supabase và trang chủ thật ngày 28/10/2026 sau khi nhóm chạy SQL thành công:

| SKU | Giá gốc | Giảm | Giá bán |
|---|---:|---:|---:|
| `A-021-DEN-M` — Boxer nam cotton | 129.000đ | 25% | 96.750đ |
| `1-023-DEN-S` — Boxer nam thể thao | 189.000đ | 25% | 141.750đ |

Cả hai hiện nhãn −25% và giá gốc gạch ngang ở trang chủ. Tại thời điểm kiểm tra, hai SKU chưa có URL ảnh nên website dùng ảnh chờ cập nhật.

## Thanh toán Visa, VNPay, ZaloPay

Mã đã có: khởi tạo giao dịch, chuyển hướng sang nhà cung cấp, IPN/callback, đối chiếu chữ ký/mã merchant/số tiền/mã giao dịch, truy vấn lại trạng thái, chống ghi nhận trùng. Visa đi qua kênh thẻ quốc tế của VNPay, cần merchant được nhà cung cấp bật quyền tương ứng. Website không thu số thẻ hoặc CVV.

Hiện nhóm chưa có thông tin tích hợp và HTTPS công khai nên ba phương thức vẫn bị khóa. Các kiểm thử sử dụng dữ liệu giả và PostgreSQL cục bộ; chưa có giao dịch được gửi đến sandbox thật.

1. Chạy `database/Supabase-Online-Payments.sql` sau `Supabase-Web-Payments.sql` trên Supabase SQL Editor. File không tự xác nhận hóa đơn cũ.
2. Cung cấp HTTPS cho website và API, trỏ về đúng hai ứng dụng. Hai URL này phải ổn định trong suốt lúc thanh toán; đổi tunnel giữa chừng có thể làm mất thông báo.
3. Khi nhận thông tin merchant, chạy trong PowerShell:

```powershell
.\scripts\Configure-PaymentSandbox.ps1 -VNPay -ZaloPay -Visa
.\scripts\Start-SupabaseCatalog.ps1 -EnableOrders -OrderEmployeeId "NV-001" -EnableBankTransfer
```

Có thể chỉ dùng `-VNPay` hoặc `-ZaloPay`, chưa bật `-Visa` nếu merchant chưa được chấp nhận thẻ quốc tế. Script hỏi URL, mã merchant và khóa ở ô ẩn. Chỉ giữ cấu hình trong PowerShell đang mở, không ghi khóa vào file/Git. Backend cần được mở lại từ chính cửa sổ này để nhận cấu hình mới.

Các biến tương ứng: `Payments__Sandbox`, `Payments__PublicApiBaseUrl`, `Payments__StorefrontBaseUrl`, `Payments__VNPay__Enabled`, `Payments__VNPay__EnableInternationalCards`, `Payments__VNPay__TmnCode`, `Payments__VNPay__HashSecret`, `Payments__ZaloPay__Enabled`, `Payments__ZaloPay__AppId`, `Payments__ZaloPay__Key1`, `Payments__ZaloPay__Key2`. Mặc định sandbox; không chuyển production trước khi kiểm thử và được nhà cung cấp duyệt. Không đổi merchant/khóa khi còn giao dịch đang chờ nếu chưa có phương án đối soát chúng.

Đăng ký các địa chỉ với nhà cung cấp (thay `API_HTTPS` bằng URL API thật):

| Chức năng | Địa chỉ |
|---|---|
| VNPay IPN | `API_HTTPS/api/payments/vnpay/ipn` |
| VNPay Return | `API_HTTPS/api/payments/vnpay/return` |
| ZaloPay callback | `API_HTTPS/api/payments/zalopay/callback` |
| ZaloPay Return | `API_HTTPS/api/payments/zalopay/return` |

Không yêu cầu đăng nhập khách ở các callback; bảo vệ bằng chữ ký từ nhà cung cấp. Browser Return chỉ điều hướng, không xác nhận tiền. Nút **Kiểm tra kết quả thanh toán** hỏi lại máy chủ nhà cung cấp; khách phải sở hữu đơn. Nếu thông báo và thao tác kiểm tra đến cùng lúc, database chỉ trừ kho/cộng điểm một lần.

Đơn trả trước ban đầu có `loaihoadon=WEB_CHO_THANH_TOAN`, `trangthaithanhtoan=CHUA_THANH_TOAN`. Sau khi xác minh tiền và đủ kho: chuyển `GIAO_HANG`, `DA_THANH_TOAN`, trừ cả `soluong` và `soluongtonkho`, cộng chi tiêu/điểm đúng một lần. WinForms hiện có đọc `GIAO_HANG` trong danh sách giao hàng; thông tin phương thức giữ `VISA`, `VNPAY` hoặc `ZALOPAY`. Partner nên bổ sung nhãn ba mã này nếu giao diện còn hiển thị mã thô.

Nếu đã nhận tiền mà kho/hóa đơn không còn hợp lệ, lưu biên nhận `REVIEW`, giữ đơn cần cửa hàng xử lý, không trừ kho âm. Khoản trả lặp qua giao dịch khác cũng lưu riêng để đối soát/hoàn tiền; không tự hoàn tiền. Nhân viên xử lý các ca này từ báo cáo `gateway_receipt`, không bấm xác nhận chuyển khoản thủ công lần nữa.

```sql
SELECT a.sohd,a.method,r.transaction_id,r.amount,r.outcome,r.received_at
FROM webstore.gateway_receipt r JOIN webstore.gateway_attempt a ON a.id=r.attempt_id
ORDER BY r.received_at DESC LIMIT 30;
```

Một đường dẫn thanh toán được dùng lại trong 15 phút. Sau timeout khi tạo giao dịch, chờ/kiểm tra kết quả trước khi tạo lại; không tạo thêm đơn hàng để thử. Hoàn tiền tự động và job đối soát định kỳ chưa nằm trong thay đổi này; đối soát có thể thực hiện theo nút trên đơn hàng.

Tham khảo giao thức chính thức: [VNPay Pay/IPN](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html), [VNPay truy vấn](https://sandbox.vnpayment.vn/apis/docs/truy-van-hoan-tien/querydr%26refund.html), [ZaloPay tạo đơn](https://developer.zalopay.vn/v2/general/overview.html), [ZaloPay callback](https://docs.zalopay.vn/docs/developer-tools/knowledge-base/callback/).
