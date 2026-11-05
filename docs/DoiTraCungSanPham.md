# Đổi / trả đơn web đã hoàn thành — thay đổi local

Web đã có yêu cầu đổi/trả trước lượt sửa này, nhưng khách phải nhập SKU. Luồng mới dùng các lựa chọn theo sản phẩm đã mua và dùng chung `phieudoitra` / `chitietdoitra` với WinForms. Không tạo bảng riêng.

## Luồng sử dụng

1. Khách đăng nhập → Đơn hàng → chi tiết đơn đã hoàn thành → **Đổi / trả sản phẩm này**. API chỉ nhận đơn WEB thuộc khách, đã giao thành công, thanh toán đầy đủ và còn hạn 15 ngày theo chính sách hiện có.
2. Chọn **Đổi sản phẩm** hoặc **Trả sản phẩm, hoàn tiền**. Mặc định sản phẩm, màu, size, lượng hàng gửi lại và nhận mới theo dòng đã mua. Nếu đã có yêu cầu khác, chỉ cho gửi phần số lượng còn hợp lệ.
3. Đổi hàng chỉ chọn biến thể cùng `masp`: màu hoặc size. Ô **Số lượng sản phẩm** chỉ đọc, nền trong suốt/chữ mờ và luôn bằng lượng hàng cũ gửi lại; mặc định theo dòng hàng đã mua còn được đổi/trả. Không mua thêm trong form đổi trả. Có thông tin liên hệ và địa chỉ nhận hàng đổi; sửa địa chỉ không thay đơn gốc.
4. Giao diện đã bỏ khối chênh lệch dự kiến và chú thích “đã mua / còn được đổi trả”. Phần tài khoản ngân hàng luôn hiện. Sau khi gửi yêu cầu, trang danh sách có nút **Quay lại trang chủ** để khách mua thêm bằng hóa đơn khác. API web và bước duyệt trên form đều yêu cầu lượng hàng đổi bằng lượng hàng gửi lại; request sửa thủ công hoặc phiếu chờ cũ sai số lượng bị từ chối, không thay đổi tồn.
5. Gửi yêu cầu vào hai bảng dùng chung, trạng thái `CHO_DUYET`. Form → **Đổi trả → Yêu cầu từ website** xem phân loại, hai số lượng riêng, liên hệ, địa chỉ và tài khoản hoàn nếu đã bổ sung. API duyệt kiểm tra lại cùng sản phẩm và tồn thực tế; không tin riêng giao diện web.
6. Duyệt → `HOAN_TAT` nghĩa là **đã duyệt phiếu**, không có nghĩa đã nhận hàng hoặc đã chuyển tiền. Theo hành vi form hiện có, hàng đổi mới bị trừ tồn khi duyệt. Hàng cũ chỉ cộng tồn sau bước **Nhận và kiểm hàng** xác nhận lượng đạt; có thể nhận từng phần, ghi nhật ký và chống gửi lặp.
7. Khách vào **Yêu cầu đổi / trả** xem kết quả và tiến độ kiểm nhận. Phiếu được duyệt có khoản hoàn sẽ hiện form bổ sung chủ tài khoản, số tài khoản và ngân hàng theo đúng mã phiếu. Khách không tự xác nhận đã chuyển tiền; số tài khoản được che khi trả về web và cho nhân viên không có quyền cửa hàng trưởng.

Các bước vận chuyển hàng đổi và thực hiện chuyển tiền/thu thêm vẫn cần cửa hàng xử lý. Lượt này không bổ sung cổng thanh toán hay tự ghi nhận tiền chỉ từ việc gửi/duyệt/kiểm hàng.

## Database và triển khai

- Script mới: `database/Supabase-DoiTra-CungSanPham.sql`, cùng nội dung với `database/BoSung-ThongTinDoiTraWeb.sql` ở repo form. Bổ sung nếu thiếu ba cột liên hệ `tennguoinhandoi`, `sodienthoaidoi`, `diachidoihang` và bốn cột tài khoản/ngày hoàn tiền. Nếu bốn cột hoàn tiền đã có từ luồng hủy đơn thì giữ nguyên.
- Phần kiểm nhận trước đó ở form có script riêng `database/BoSung-KiemNhanDoiTra.sql`: ba cột số lượng/nhật ký trên `chitietdoitra`. Script này dựa trên các cột trạng thái/nhân viên/ngày kiểm nhận đã có của schema form hiện tại.
- Hai API phải trỏ cùng database đã có schema tương thích của cả hai repo. Áp dụng các script bổ sung còn thiếu **một lần trên database local đã chọn**, rồi chạy API mới. API form chỉ kiểm tra cấu trúc, không tự chạy SQL.
- Sau yêu cầu “thêm và build”, đã áp dụng đúng hai script trên Supabase đang cấu hình vào ngày 05/11/2026, trong một transaction. Trước/sau vẫn **18 bảng**, bổ sung đúng 6 cột đang thiếu (4 cột hoàn tiền đã có). Các cột nghiệp vụ cũ và tồn kho giữ nguyên; chỉ các số đếm kiểm nhận được backfill theo script. Đã kiểm tra đầy đủ schema contract, khóa chính, default và trigger bảo mật của form.
- Đã build lại bản chạy chính của MVC web, backend web và WinForms (kèm API). Chưa commit/push. Không tạo đơn, phiếu đổi trả hoặc thực hiện chuyển tiền thử trên Supabase thật.

## Kiểm chứng ngày 05/11/2026

- Web backend và MVC .NET Framework 4.8 build thành công. API form và WinForms build ở thư mục artifacts riêng.
- Toàn bộ bộ test web: **213 passed, 19 skipped**; các ca bỏ qua cần fixture SQL Server/legacy riêng. Native-18 PostgreSQL chạy thật, gồm đăng nhập HTTP, ownership, số lượng, quá hạn, từ chối khác mặt hàng, tồn thay đổi lúc duyệt và liên thông repository form.
- Bốn ca dữ liệu local sau khi siết số lượng: đều gửi lại 2 sản phẩm; đổi 2 cùng giá → ngang tiền; đổi 2 biến thể giá 150.000/cái → thu thêm 108.000; đổi 2 biến thể giá 50.000/cái → hoàn 108.000; trả 2 → hoàn 216.000 (giá mua mẫu 100.000/cái, VAT 8%). Chặn đổi 1 hoặc 3 khi gửi lại 2 ở cả API tạo và duyệt. Kiểm tra gửi/duyệt lặp, chỉ trừ hàng mới một lần, kiểm hàng cũ đạt mới cộng lại và giữ nguyên khoản hoàn.
- Form: **151 checks** database/concurrency/HTTP (gồm 32 authorization checks); **22 kiểm tra** WebView đổi trả và **27 kiểm tra** UI hồi quy.
- Lượt điều chỉnh giao diện tiếp theo: Web Razor chạy thực trên IIS Express với API giả riêng, **23 kiểm tra** desktop/mobile qua: ngân hàng luôn hiện, số lượng chỉ đọc không tăng thêm, bỏ chú thích/chênh lệch, màu/size, hết tồn, giữ dữ liệu sau lỗi, gửi đúng số lượng và nút về trang chủ. Không có lỗi JavaScript hoặc tràn ngang mobile. Không kết nối/chạy SQL hoặc thay API/database ở lượt chỉnh UI này.
- Báo cáo backend local: `tmp/web-return-test-results/`; giao diện mới: `tmp/web-return-ui-revision/results.json`, ảnh `exchange-desktop.png`, `exchange-mobile.png` trong cùng thư mục. Phía form: `artifacts/return-inspection/web-flow-checks.log`, `ui/results.json`.

## Bản chạy sau migration

- Web backend: `FashionStore.Backend/bin/Debug/net8.0/`; MVC: `FashionStore.Web/bin/FashionStore.Web.dll`.
- Form: `../Winform_FashionStore/QLIShopThoiTrang/winform/qlicuahang/bin/Debug/net8.0-windows/qlicuahang.exe`; API mới nằm trong thư mục `api` cạnh file này.
- Đã bật provider Supabase và hai cờ customer/order trong **user-secrets local**, giữ nguyên các khóa thanh toán. Connection dùng chung `YODYSTORE_CONNECTION_STRING` đã lưu; không thêm mật khẩu vào source.
- API web cổng 5000 và API form cổng 5001 đã kiểm tra health thành công với Supabase; catalog web cũng đọc được dữ liệu thật. API form cần ASP.NET Core 10, máy hiện chưa có shared ASP.NET Core 10 trong thư mục runtime hệ thống nên đang chạy bằng runtime đầy đủ có sẵn trong `tmp/dotnet-sdk10`. Không cài runtime toàn máy.
- Nếu khởi động lại máy, chạy `tmp/start-return-services.ps1` ở repo web rồi mở form. Script dùng runtime local có sẵn và khởi động cả hai API ở chế độ ẩn; chỉ tái sử dụng API nếu health đang tốt.
- Biên nhận migration: `tmp/return-schema-upgrade-report/applied.json`; kiểm tra sau build: `tmp/return-schema-upgrade-report/post-build/preflight.json`. Test mới: `tmp/web-return-test-results/returns-ready-isolated.trx`, **213 passed / 19 skipped**, và `artifacts/return-inspection/returns-ready-checks.log` phía form, **151 checks**. Test hồi quy đặt cấu hình provider của process riêng và bỏ connection live khỏi process test; fixture PostgreSQL chỉ dùng loopback.

## Chạy lại UI fixture

Trong thư mục gốc repo web, build `verification/FashionStore.MvcCompileCheck.csproj` với `AssemblyName=FashionStore.Web` và `OutDir=<đường dẫn tuyệt đối repo>\tmp\web-return-mvc\`; sau đó chạy `verification/customer-returns/start-preview.ps1` và `node verification/customer-returns/browser.mjs`.

Fixture dùng IIS Express cổng 58497, mock API 5039, Edge headless và thư viện Playwright từ Codex runtime của máy hiện tại. Các tiến trình do fixture tạo có PID trong thư mục ghi ở `tmp/web-return-preview-path.txt`; dừng hai PID khi kiểm tra xong. Đây là kiểm tra UI với dữ liệu giả, không thay thế kiểm thử PostgreSQL thật ở trên. Bản preview copy site vào `tmp`, không đổi cấu hình site chính.

Chạy kiểm thử liên thông backend với `PartnerSourceRoot` trỏ repo form, biến `FASHIONSTORE_POSTGRES_TEST_CONNECTION` trỏ `fashionstore_test` trên PostgreSQL loopback và test `Native18IntegrationTests`. Fixture tự tạo database có tên ngẫu nhiên và xóa trong `finally`; không được dùng connection Supabase/live.
