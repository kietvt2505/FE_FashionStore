# BÁO CÁO TIẾN ĐỘ XÂY DỰNG WEBSITE BÁN HÀNG THỜI TRANG YODY

**Thời điểm báo cáo: 27/10/2026**

Thưa thầy/cô, nhóm em xin báo cáo tiến độ phần website bán hàng trong hệ thống quản lý cửa hàng thời trang YODY. Website phục vụ khách hàng xem sản phẩm, quản lý tài khoản và đặt hàng trực tuyến; ứng dụng WinForms phục vụ nhân viên xử lý nghiệp vụ tại cửa hàng. Hai phần được xây dựng để sử dụng chung cơ sở dữ liệu Supabase.

## 1. Nền tảng và kết nối dữ liệu

Website sử dụng ASP.NET MVC cho giao diện, ASP.NET Core cho API và PostgreSQL trên Supabase để lưu dữ liệu. Nhóm đã kết nối thành công với cơ sở dữ liệu chung và đọc được sản phẩm thực tế. Đơn hàng từ website được ghi vào các bảng hóa đơn, chi tiết hóa đơn dùng chung với WinForms; các dữ liệu riêng của website được lưu bổ sung và liên kết theo mã khách hàng, sản phẩm, SKU và hóa đơn.

Hiện website được kiểm thử trên môi trường local có kết nối Supabase. Việc nghiệm thu toàn bộ quy trình trên hai máy chạy website và WinForms vẫn cần thực hiện thêm.

## 2. Những chức năng đã triển khai

| Nhóm chức năng | Nội dung đã thực hiện |
| --- | --- |
| Giao diện khách hàng | Trang chủ bằng tiếng Việt; thống nhất font chữ; menu Nam, Nữ và Phụ kiện có dropdown; bố trí các trang sản phẩm, tài khoản, giỏ hàng và thanh toán. |
| Danh sách sản phẩm | Đọc sản phẩm từ Supabase; tìm kiếm; lọc theo đối tượng, loại sản phẩm, khoảng giá, kích cỡ, màu và tồn kho; sắp xếp và phân trang. Một sản phẩm có nhiều SKU được gộp thành một thẻ. |
| Chi tiết sản phẩm | Chọn màu và kích cỡ riêng; hiển thị ảnh theo màu; kiểm tra giá và tồn của biến thể tương ứng. |
| Tài khoản | Đăng ký, đăng nhập, đăng xuất, xem/sửa hồ sơ và đổi mật khẩu. Mật khẩu được lưu dạng băm; dữ liệu cá nhân được giới hạn theo tài khoản. |
| Địa chỉ và yêu thích | Thêm, sửa, xóa địa chỉ nhận hàng; lưu và bỏ lưu sản phẩm yêu thích. |
| Giỏ hàng | Thêm sản phẩm theo SKU, cập nhật số lượng, xóa từng sản phẩm hoặc toàn bộ giỏ; tính tiền và kiểm tra lại giá, tồn kho khi đặt hàng. |
| Đặt hàng | Nhập người nhận, điện thoại, địa chỉ và ghi chú; hỗ trợ COD và chuyển khoản chờ nhân viên xác nhận. |
| Ưu đãi thành viên | Website tự tính giảm giá theo hạng trước khi tạo đơn; lưu mức giảm và số tiền khách phải trả cùng hóa đơn. |
| Theo dõi đơn | Lịch sử và chi tiết đơn; ảnh đúng màu sản phẩm đã mua; trạng thái giao hàng, thanh toán và tiến trình xử lý. Trạng thái được cập nhật khi tải lại trang. |
| Mã đơn và thời gian | Đơn mới dùng mã ngắn dạng WEB-000001; có chống tạo trùng khi gửi lại cùng yêu cầu. Đã sửa cách hiển thị ngày giờ theo giờ Việt Nam UTC+7. |
| Đánh giá và yêu cầu đổi/trả | Đã xây dựng và kiểm thử phần khách gửi đánh giá theo đơn đủ điều kiện, gửi yêu cầu đổi/trả và xem trạng thái. Phần nhân viên duyệt và xử lý hoàn tất còn cần tích hợp. |

Các chức năng trong bảng đã có trong mã nguồn; mức độ kiểm chứng trên Supabase chung và WinForms thật được trình bày ở các phần sau.

## 3. Quy tắc ưu đãi và thanh toán

Website áp dụng cùng quy tắc xét hạng đang có trong mã WinForms:

| Tổng chi tiêu trước đơn hiện tại | Hạng | Mức giảm |
| --- | --- | --- |
| Đến hết 5.000.000đ | Bạc | 0% |
| Trên 5.000.000đ đến hết 15.000.000đ | Vàng | 5% |
| Trên 15.000.000đ | Kim Cương | 10% |

Website tự áp dụng ưu đãi theo hạng, không yêu cầu khách nhập thêm mã khuyến mãi. Điểm tiếp tục tích lũy theo mức 10.000đ = 1 điểm khi giao dịch được ghi nhận đủ điều kiện; hiện chưa sử dụng điểm để đổi thành tiền.

Với COD, hóa đơn ghi số tiền cần thu sau giảm để WinForms tiếp nhận. Với chuyển khoản, đơn được lưu ở trạng thái chờ đối soát; việc khách chọn chuyển khoản chưa đồng nghĩa đã thanh toán. Số tiền, giá và điều kiện ưu đãi được kiểm tra tại backend khi tạo đơn.

## 4. Những phần chưa hoàn thiện hoặc chưa kiểm chứng đầy đủ

| Nội dung | Trạng thái hiện tại |
| --- | --- |
| Xác nhận chuyển khoản | Đã có xử lý xác nhận và kiểm tra số tiền phía database, nhưng chưa có kết nối tự động với ngân hàng. Cần hoàn thiện thao tác nhân viên tiếp nhận, đối soát đơn chờ trong WinForms. |
| Giữ hàng cho COD | Có kiểm tra tồn lúc đặt, nhưng chưa có cơ chế giữ chỗ tồn kho dùng chung giữa website và POS cho đơn chờ giao. |
| Duyệt đánh giá, xử lý đổi/trả | Phần khách gửi yêu cầu đã có. WinForms chưa có màn hình tích hợp để duyệt các yêu cầu từ website và liên kết đầy đủ với phiếu xử lý thực tế. |
| Khách tự hủy đơn, hoàn tiền | Chưa mở quy trình tự xử lý hoàn chỉnh; hiện khách cần liên hệ cửa hàng. |
| Email quên mật khẩu | Đã có xử lý liên kết khôi phục và kiểm thử, nhưng chưa cấu hình SMTP và địa chỉ HTTPS để kiểm tra gửi email thật. |
| Quản trị website trên Supabase | Các chức năng quản trị cũ chưa được chuyển đầy đủ sang Supabase; API quản trị chưa được bật trong chế độ này. |
| Vận chuyển | Chưa kết nối đơn vị vận chuyển và chưa có cách tính phí giao hàng thực tế; phí hiện đang là 0đ. |
| Cập nhật tức thời | Hiện đọc lại trạng thái khi tải trang; chưa triển khai cập nhật thời gian thực. |
| Kiểm thử và triển khai thực tế | Chưa nghiệm thu trọn quy trình COD và chuyển khoản giữa website và WinForms của partner trên hai máy; chưa có kết quả kiểm thử vận hành trên môi trường triển khai công khai. |

## 5. Kết quả kiểm tra và công việc tiếp theo

Nhóm đã kiểm tra kết nối Supabase, hiển thị sản phẩm thật và tạo được đơn hàng trên website. Lịch sử đã xuất hiện mã đơn ngắn WEB-000001.

Lần kiểm thử tự động gần nhất có **98 kiểm thử đạt**, **15 kiểm thử dành cho SQL Server được bỏ qua do chưa cấu hình cơ sở dữ liệu kiểm thử tương ứng**. Các kiểm thử có kiểm tra quyền sở hữu dữ liệu, giá và tồn kho, ưu đãi theo hạng, chống tạo trùng đơn, số tiền sau giảm, xác nhận thanh toán lặp và chuyển đổi giờ UTC sang giờ Việt Nam. Giao diện MVC/Razor cũng đã biên dịch thành công. Kết quả này chưa thay thế việc nghiệm thu liên thông với ứng dụng WinForms thật.

Trong giai đoạn tiếp theo, nhóm ưu tiên:

1. Kiểm thử một đơn COD và một đơn chuyển khoản với WinForms trên hai máy; đối chiếu mã đơn, tiền giảm, tiền phải trả và trạng thái.
2. Hoàn thiện việc nhân viên tiếp nhận và xác nhận các đơn chuyển khoản chờ thanh toán.
3. Thống nhất cơ chế giữ hàng COD, thời điểm trừ tồn và cộng điểm khi xử lý đồng thời với bán tại quầy.
4. Hoàn thiện màn hình xử lý yêu cầu đổi/trả, duyệt đánh giá và cấu hình email khôi phục mật khẩu.
5. Chuẩn bị môi trường triển khai, kiểm thử tổng thể và tài liệu hướng dẫn sử dụng.

Ở thời điểm hiện tại, website đã có luồng khách hàng từ xem sản phẩm, chọn biến thể, đưa vào giỏ, đặt hàng đến theo dõi đơn. Trọng tâm còn lại là hoàn thiện các bước xử lý nghiệp vụ phía nhân viên và kiểm chứng sự phối hợp giữa website, Supabase và WinForms.
