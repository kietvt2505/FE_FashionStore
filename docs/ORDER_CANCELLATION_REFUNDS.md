# Hủy đơn online và tiếp nhận hoàn tiền

## Cài đặt

Sau các file hủy đơn và thanh toán đã cài trước đó, chạy toàn bộ `database/Supabase-Order-Cancellation-Refunds.sql` trong Supabase SQL Editor. Hai cột cuối file `co_yeu_cau_hoan_tien` và `co_huy_kem_ly_do` phải cùng `true`. Chạy file này sau cùng nếu chạy lại các bản SQL hủy đơn cũ.

Khởi động lại backend, tải lại website bằng Ctrl + F5. Không cần đổi cấu hình hay khóa cổng thanh toán. File cài đặt không hủy, hoàn tiền hay sửa số lượng của các đơn hiện có.

## Luồng khách hàng

- Chỉ được tự hủy ở **Chờ xác nhận** hoặc **Đã xác nhận**. Bắt đầu chuẩn bị / chờ lấy hàng, đã lấy hàng, đang giao và giao xong đều bị chặn, kể cả gửi yêu cầu từ trang cũ.
- COD chưa trả tiền: xác nhận hủy, có thể nhập lý do; không yêu cầu tài khoản ngân hàng.
- Online / chuyển khoản chưa trả tiền: bắt buộc lý do từ 5–500 ký tự; không thu thập tài khoản ngân hàng khi chưa phát sinh tiền cần hoàn.
- Online / chuyển khoản đã trả tiền: khách nhập lý do, tên chủ tài khoản, số tài khoản và ngân hàng trong **một hộp thoại**, rồi xác nhận. Hệ thống kiểm tra lại quyền sở hữu, bước giao hàng và bằng chứng thanh toán tại database.
- Sau khi hủy thành công, đơn vẫn giữ hóa đơn, sản phẩm, thời gian và lý do. Khách thấy **Đang chờ cửa hàng hoàn tiền**, số tiền và số tài khoản che bớt chỉ còn 4 số cuối.
- Nếu tiền xác minh đến sau khi đơn chưa trả tiền đã bị hủy, hệ thống giữ giao dịch ở REVIEW, không giao lại / trừ kho / cộng điểm. Website hiện yêu cầu bổ sung tài khoản nhận tiền, chuyển từ CHO_THONG_TIN sang CHO_HOAN_TIEN sau khi gửi đủ thông tin.

Website không tự chuyển tiền. Khách không phải cung cấp mật khẩu, OTP hay thông tin bảo mật thẻ.

## Điều chỉnh kho và thành viên

Đơn COD hoặc online chưa thanh toán chưa từng trừ kho hay cộng điểm nên hủy không thay đổi các số dư.

Với đơn đã thanh toán, chỉ chấp nhận khi có biên nhận PAID của cổng hoặc bản đối soát chuyển khoản `payment_confirmation` đúng số tiền. Hệ thống đối chiếu SKU/số lượng trong hóa đơn với snapshot trước khi hoàn lại cả hai cột tồn kho, trừ đúng điểm / chi tiêu của đơn, tính lại hạng và ghi các điều chỉnh trong `order_cancellation`. Nếu hóa đơn bị sửa hoặc số dư điểm không đủ để điều chỉnh, toàn bộ thao tác hủy bị từ chối để nhân viên kiểm tra; không cập nhật nửa chừng.

Tất cả dùng cùng khóa dòng hóa đơn với xác nhận thanh toán. Bấm hủy nhiều lần chỉ tạo một yêu cầu, không hoàn kho hay trừ điểm nhiều lần. Thanh toán vẫn là DA_THANH_TOAN trong lúc chờ hoàn; chỉ sau khi nhân viên xác nhận giao dịch chuyển lại mới thành HOAN_TIEN.

## Partner kết nối WinForms

Đã chuẩn bị dữ liệu và các hàm dưới đây. **Bản WinForms hiện có chưa có màn hình duyệt/xác nhận hoàn tiền**; partner cần bổ sung màn hình đọc hàng đợi và gọi hàm xác nhận sau khi đối soát. Không gọi các hàm này trực tiếp từ trình duyệt, không cấp quyền cho anon/authenticated và không đưa khóa database vào giao diện.

API riêng của WinForms dùng role database có quyền phù hợp, đọc view riêng:

```sql
SELECT sohd,makh,reason,amount,status,account_holder,account_number,bank_name,
       createdat,submittedat,refundedat,employeeid,refund_reference,
       stock_restored,reversed_points,reversed_spend
FROM webstore.customer_refund_queue
WHERE status IN ('CHO_THONG_TIN','CHO_HOAN_TIEN')
ORDER BY createdat;
```

CHO_THONG_TIN: chờ khách cung cấp tài khoản. CHO_HOAN_TIEN: đã đủ thông tin, chờ nhân viên chuyển tiền và đối soát. DA_HOAN_TIEN: đã ghi nhận giao dịch hoàn tiền.

Sau khi **thực sự chuyển tiền và kiểm tra kết quả**, API WinForms gọi hàm bằng tham số:

```sql
SELECT webstore.complete_customer_refund(@order_id,@employee_id,@bank_reference);
```

Hàm yêu cầu nhân viên đang hoạt động, tài khoản nhận đã đủ và mã giao dịch ngân hàng. Hàm chỉ ghi nhận kết quả, không gửi lệnh chuyển tiền. Gọi lại cùng mã trả ALREADY_REFUNDED; mã khác sau khi hoàn bị chặn. Không lấy số tiền hoặc trạng thái hoàn tiền từ dữ liệu khách tự gửi.

Tài khoản đầy đủ chỉ lưu ở bảng riêng `webstore.order_refund`, bật RLS và thu hồi quyền PUBLIC/anon/authenticated. API khách chỉ đọc được đơn của mình và chỉ nhận số tài khoản đã che. Giữ thông tin này trong API nội bộ của WinForms, không trả ra danh sách khách khác hoặc log nội dung biểu mẫu.

## Năm bước giao hàng

| Bước | COD | Online / chuyển khoản |
|---|---|---|
| 1 | Đang chờ xác nhận | Đang chờ xác nhận thanh toán |
| 2 | Đơn đã được xác nhận | Đơn đã được xác nhận (Thanh toán thành công) |
| 3 | Đơn vị vận chuyển lấy hàng thành công | Đơn vị vận chuyển lấy hàng thành công |
| 4 | Đơn vị vận chuyển đang giao hàng | Đơn vị vận chuyển đang giao hàng |
| 5 | Giao hàng thành công | Giao hàng thành công |

COD mới lưu `Chờ xác nhận` / MOI. Khi WinForms cập nhật `Vừa xác nhận đơn`, website hiển thị bước 2. Các tên trạng thái gốc của partner (`Shipper đã lấy hàng`, `Hàng đang được vận chuyển`, `Hàng giao thành công`) vẫn đọc được, không đổi các đơn cũ. Bước chuẩn bị/chờ lấy hàng được gộp vào bước đã xác nhận khi trình bày, nhưng vẫn khóa quyền hủy theo trạng thái thực ở database.

Online mới chờ thanh toán; chỉ biên nhận đã xác minh hoặc nhân viên đối soát chuyển khoản mới đưa sang bước xác nhận. Bước đã qua mờ, bước hiện tại đậm; không tạo ngày giờ giả cho các mốc chưa được ghi nhận.
