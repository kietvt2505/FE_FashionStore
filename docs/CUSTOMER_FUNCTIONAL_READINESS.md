# Rà soát chức năng website — 27/10/2026

**Chính sách mới đã chốt:** website chỉ tự giảm theo hạng Bạc 0%, Vàng 5%, Kim Cương 10%; chưa đổi điểm thành tiền và không nhập thêm mã. Hướng dẫn hiện hành là [SUPABASE_MEMBERSHIP_PRICING.md](SUPABASE_MEMBERSHIP_PRICING.md), dùng `Supabase-Membership-Pricing.sql`. Các mục 1–4 và phần bật mã bên dưới ghi lại phiên bản voucher trước đây, đã được thay thế, không dùng để bật mã trên website nữa. Những chức năng tài khoản và các hạn chế còn lại vẫn áp dụng.

## Những phần hoàn thiện trong lượt này

1. **Mã ưu đãi khi thanh toán:** kiểm tra thời gian hoạt động, mức mua tối thiểu, giảm theo phần trăm hoặc số tiền. Chỉ áp dụng một mã; tính trên giá bán hiện tại trong giỏ. Kiểm tra lại giá, tồn và điều kiện mã khi bấm đặt hàng, không lấy số tiền do trình duyệt gửi lên. Chưa nhận đơn miễn phí có tổng bằng 0.
2. **Lưu đúng số tiền sang WinForms:** `public.hoadon.tongcong` là tiền hàng, `chietkhau` là số tiền giảm, `khachphaitra` là số khách trả, `makm` là mã canonical trong database; COD lấy số tiền sau giảm vào `tiencod`. Không thay cách WinForms trừ kho/cộng điểm. Xác nhận chuyển khoản dùng số tiền sau giảm và cộng điểm một lần, theo quy trình đã có.
3. **Lưu giá lúc mua:** `webstore.order_pricing` giữ tiền hàng, mức giảm và mã đã dùng. Giá catalog hoặc chương trình thay đổi sau đó không làm thay đổi phần thông tin này trong lịch sử đơn. Retry cùng mã yêu cầu trả lại đơn cũ, kể cả khi ưu đãi sau đó hết hạn.
4. **Trang Ưu đãi nối tới Checkout:** bấm “Áp dụng mã này” để điền sẵn mã và xem số tiền giảm. Nút “Kiểm tra mã” cũng hoạt động khi chưa nhập xong địa chỉ nhận hàng.
5. **Sửa địa chỉ đã lưu:** biểu mẫu lấy dữ liệu địa chỉ thuộc tài khoản đang đăng nhập; cập nhật ngay bản ghi cũ, có thể sửa khi đã lưu đủ 20 địa chỉ. Tài khoản khác không thể sửa địa chỉ bằng cách đoán ID.
6. **Đánh giá từ đơn hàng:** đơn đã giao và thanh toán có liên kết “Viết đánh giá” ở từng sản phẩm. Trang đánh giá điền sẵn mã đơn; backend tiếp tục kiểm tra quyền mua hàng và chống đánh giá trùng.
7. **Cập nhật trạng thái:** bổ sung liên kết tải lại tình trạng đơn để đọc thay đổi từ WinForms.
8. **Thông tin chuyển khoản:** lấy cấu hình nhận tiền từ hàm `buildVietQrUrl` trong mã WinForms đã giải nén, không tự tạo số tài khoản. File ZIP gốc không còn tại đường dẫn đã gửi nên đợt kiểm tra này dựa vào thư mục `Winform_FashionStore` đang có. BIN `970423` được đối chiếu là TPBank theo [danh sách ngân hàng VietQR](https://www.vietqr.io/api-bank/demo). Chưa tìm thấy SMTP trong bản mã nguồn được cung cấp.

## Cách bật mã ưu đãi trên database của nhóm

1. Các file tài khoản/giỏ hàng, đơn hàng và tính năng tài khoản của đợt trước vẫn cần được áp dụng. Nếu dùng chuyển khoản, giữ migration `Supabase-Web-Payments.sql`.
2. Sao chép toàn bộ `database/Supabase-Checkout-Promotions.sql` vào Supabase SQL Editor và Run. File chỉ thêm bảng lưu giá website với RLS/quyền riêng; không sửa dữ liệu hóa đơn cũ, số dư điểm, chương trình hoặc tồn kho. Có thể chạy lại.
3. Dừng backend cũ rồi khởi động lại:

```powershell
.\scripts\Start-SupabaseCatalog.ps1 -EnableOrders -OrderEmployeeId "NV-001" -EnableBankTransfer
```

4. Chạy lại website nếu cần và tải lại trình duyệt. Nếu chưa áp migration mới, website vẫn cho đặt đơn không dùng mã; mục nhập mã được ẩn và API từ chối mã ưu đãi.

Kiểm tra bằng SELECT:

```sql
SELECT to_regclass('webstore.order_pricing') AS bang_luu_gia;

SELECT makm, tenchuongtrinh, loaikm, giatrigiam, giatridonhangtoithieu,
       ngaybatdau, ngayketthuc
FROM public.khuyenmai
WHERE trangthai AND CURRENT_DATE BETWEEN ngaybatdau AND ngayketthuc
ORDER BY ngayketthuc, makm;
```

Kiểu giảm website nhận: `Phần trăm`/`PHAN_TRAM`/`phan tram`/`%`, hoặc `Số tiền`/`SO_TIEN`/`so tien`/`VND`, không phân biệt hoa thường. Giá trị phần trăm phải trong khoảng lớn hơn 0 đến 100. Mã có kiểu/mức giảm không hợp lệ hoặc tạo tổng bằng 0 sẽ không tạo đơn. Schema chung chưa có giới hạn lượt dùng chương trình; website không tự đặt thêm hạn mức.

Ví dụ: giỏ 200.000đ, mã giảm 10% → tiền hàng 200.000đ, giảm 20.000đ, khách trả 180.000đ. COD thu 180.000đ. Chuyển khoản chỉ xác nhận khi đối soát đúng 180.000đ. Khi ghi nhận tích điểm, số tiền sau giảm là cơ sở tính điểm theo quy trình hiện tại.

## Những phần vẫn cần cấu hình hoặc phối hợp

| Phần | Trạng thái thực tế |
|---|---|
| Gửi email khôi phục mật khẩu | Backend xử lý token đã có và đã kiểm thử; bản WinForms cung cấp không có cấu hình SMTP. Cần email gửi, SMTP và URL HTTPS của website để thử gửi thật. |
| Xác nhận tiền chuyển khoản | Chưa có kết nối biến động ngân hàng/webhook. Nhân viên đối soát rồi dùng hàm xác nhận theo `SUPABASE_PAYMENT_HANDOFF.md`; chọn chuyển khoản trên web không tự đánh dấu đã trả tiền. |
| Duyệt đánh giá, tiếp nhận yêu cầu đổi/trả từ web | Website lưu yêu cầu chờ, backend có kiểm tra quyền/điều kiện. WinForms chưa có màn hình đọc các bảng website; tạm thời cần người có quyền database phối hợp theo hướng dẫn tính năng tài khoản. |
| Hủy đơn | Mã và kiểm thử đã có: khách tự hủy đơn chưa thanh toán, chưa bàn giao vận chuyển. Cần chạy `Supabase-Customer-Order-Cancellation.sql` để bật; hóa đơn được giữ với loại WEB_HUY, không hoàn kho/cộng điểm. Xem `SUPABASE_CUSTOMER_ORDER_CANCELLATION.md`. |
| Hoàn tiền, đổi/trả hoàn tất | Khách liên hệ cửa hàng; không tự hoàn tiền hoặc hoàn tất đổi trả trên website. |
| Giữ tồn cho COD chờ giao | Chưa có cơ chế giữ chỗ dùng chung với POS. Kiểm tra tồn tại checkout không phải giữ hàng. Partner cần rà soát `WinForms-Order-Concurrency.patch` và thống nhất cơ chế trước khi chạy đồng thời thực tế. |
| Đổi điểm thành tiền | Chưa mở; chưa có quy tắc quy đổi được nhóm chốt. Số dư/hạng lấy từ hồ sơ chung; lịch sử điểm hiện chỉ có các xác nhận chuyển khoản website đã lưu. |
| Quản trị website trên Supabase | Các API quản trị cũ vẫn khóa trong chế độ Supabase; chưa chuyển cơ chế xác thực/phân quyền nhân viên SQL Server. Không coi đăng nhập khách hàng là quyền nhân viên. |
| Phí vận chuyển/kết nối hãng giao hàng | Phí hiện 0 theo cấu hình nghiệp vụ đang có; chưa có tích hợp hãng vận chuyển. |
| Chạy liên thông trên hai máy | Đã kiểm thử API bằng PostgreSQL riêng trên máy. Vẫn cần nhóm chạy thử một đơn COD và một đơn chuyển khoản trên Supabase chung cùng WinForms thật. |

## Kiểm chứng đã thực hiện

- Backend và MVC/Razor biên dịch thành công.
- Kiểm thử PostgreSQL riêng: chạy migration hai lần, mã hết hạn/chưa bắt đầu/đã tắt, mức mua tối thiểu, giảm cố định/phần trăm, dữ liệu mã sai, thiếu tồn và mã bị tắt sau bước báo giá.
- Thử gửi số tiền giả từ client: hóa đơn vẫn dùng số tiền máy chủ tính. Kiểm tra các cột hóa đơn chung, tiền COD, giá lưu trong lịch sử, retry và đối soát chuyển khoản không cộng điểm/trừ kho trùng.
- Kiểm tra sửa địa chỉ giữa hai tài khoản, sửa khi đủ 20 địa chỉ, dữ liệu sai; luồng đánh giá tiếp tục được kiểm tra theo đơn thuộc khách hàng.
- Kiểm tra trình duyệt với dữ liệu giả: sửa địa chỉ thành công, bấm áp mã từ trang Ưu đãi, báo giá khi địa chỉ còn trống, mã sai không giữ số tiền giảm cũ, mở đánh giá từ đơn được điền sẵn mã, hướng dẫn chuyển khoản hiện đúng khi đơn vẫn chưa thanh toán. Không tràn ngang ở màn hình 390px.
- Không chạy migration mới hoặc tạo đơn thử trên Supabase dùng chung trong lượt sửa code này.
