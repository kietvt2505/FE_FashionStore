using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FashionStore.Backend.Data;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Models;
using FashionStore.Backend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class Native18PostgresFactAttribute : FactAttribute {public Native18PostgresFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FASHIONSTORE_POSTGRES_TEST_CONNECTION")))Skip="Requires the local native-18 PostgreSQL fixture.";}}
public sealed class Native18IntegrationTests
{
    [Native18PostgresFact]
    public async Task NativeTablesSupportColumnsReviewsRecoveryAndOrdersWithoutNewTables()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FASHIONSTORE_POSTGRES_TEST_CONNECTION")!);
        Assert.Contains(settings.Host,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("fashionstore_test",settings.Database);
        var database="fashionstore_native18_"+Guid.NewGuid().ToString("N");settings.Database="postgres";
        await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        await using(var create=new NpgsqlCommand("CREATE DATABASE "+database,admin))await create.ExecuteNonQueryAsync();
        try
        {
            settings.Database=database;settings.Timezone="UTC";
            await using var sql=new NpgsqlConnection(settings.ConnectionString);await sql.OpenAsync();
            async Task Exec(string text){await using var c=new NpgsqlCommand(text,sql){CommandTimeout=120};await c.ExecuteNonQueryAsync();}
            async Task<T> Scalar<T>(string text){await using var c=new NpgsqlCommand(text,sql);return (T)(await c.ExecuteScalarAsync())!;}
            using var metadata=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase_public_metadata_20261103.json")));
            static string Q(string text)=>"\""+text.Replace("\"","\"\"")+"\"";
            var nativeTables=metadata.RootElement.GetProperty("relations").EnumerateArray().Where(t=>t.GetProperty("kind").GetString()=="r").ToArray();
            foreach(var table in nativeTables)
            {
                var columns=table.GetProperty("columns").EnumerateArray().Select(col=>{
                    var type=col.GetProperty("type").GetString()!;
                    var fallback=type=="boolean"?"true":type=="jsonb"?"'[]'::jsonb":type.StartsWith("timestamp")?"CURRENT_TIMESTAMP":type=="date"?"CURRENT_DATE":type is "integer" or "bigint" || type.StartsWith("numeric")?"0":"''";
                    return Q(col.GetProperty("name").GetString()!)+" "+type+(col.GetProperty("not_null").GetBoolean()?" NOT NULL DEFAULT "+fallback:"");
                });
                await Exec("CREATE TABLE public."+Q(table.GetProperty("name").GetString()!)+"("+string.Join(',',columns)+")");
            }
            foreach(var constraint in metadata.RootElement.GetProperty("constraints").EnumerateArray().OrderBy(c=>c.GetProperty("type").GetString()=="f"?1:0))
                await Exec("ALTER TABLE public."+Q(constraint.GetProperty("table").GetString()!)+" ADD CONSTRAINT "+Q(constraint.GetProperty("name").GetString()!)+" "+constraint.GetProperty("definition").GetString());
            foreach(var view in metadata.RootElement.GetProperty("view_definitions").EnumerateArray())
                await Exec("CREATE VIEW public."+Q(view.GetProperty("name").GetString()!)+" AS "+view.GetProperty("definition").GetString());
            await Exec("""
                INSERT INTO public.phanquyen(maquyen,tenquyen) VALUES('Q1','Nhan vien');
                INSERT INTO public.nhanvien(manv,hoten,sodienthoai,tendangnhap,maquyen,trangthai) VALUES('NV01','Nguoi thu','0900000099','nhanvien','Q1',true);
                INSERT INTO public.khachhang(makh,hoten,sodienthoai,tendangnhap,matkhau) VALUES('KH-OLD','Khach cu','0900000001','existing',NULL);
                INSERT INTO public.khachhang(makh,hoten,sodienthoai,email,tendangnhap,matkhau) VALUES('KH-POS','Khach tai quay','0900000002','pos@example.test',NULL,NULL);
                INSERT INTO public.loaisanpham(maloai,tenloai,trangthai) VALUES('AO','Áo thun nam',true);
                INSERT INTO public.sanpham(masp,masku,maloai,tensanpham,kichco,mausac,dongia,soluong,soluongtonkho,danhmuc,mota,danhsachhinhanh)
                VALUES('P1','SKU-M','AO','Áo thun nam','M','Đen',250000,10,10,'NAM','Mô tả có sẵn','["https://example.test/black.png"]');
                INSERT INTO public.hoadon(sohd,makh,nguondon,loaihoadon,phuongthucthanhtoan,trangthaigiaohang,trangthaithanhtoan,tongcong,khachphaitra,tennguoinhan,diachigiao,sodienthoai_kh,manv)
                VALUES('WEB-HISTORY','KH-OLD','WEB','COD','COD','Chờ xác nhận','CHUA_THANH_TOAN',250000,200000,'Khach cu','Dia chi cu hien tai','0900000001','NV01');
                INSERT INTO public.chitiethoadon VALUES('WEB-HISTORY','SKU-M',1,250000,250000);
                INSERT INTO public.khuyenmai(makm,tenchuongtrinh,loaikm,giatrigiam,ngaybatdau,ngayketthuc,trangthai) VALUES('KM1','Giam SKU','PHAN_TRAM',20,CURRENT_DATE-1,CURRENT_DATE+1,true);
                INSERT INTO public.khuyenmaisanpham VALUES('KM1','SKU-M',20);
                INSERT INTO public.giaodichthanhtoanqr(magd,sohd,nguondon,sotien,malienket,duongdanthanhtoan,manganhang,sotaikhoan,tentaikhoan,noidung,trangthai)
                VALUES(1,'POS-QR','POS',50000,'native-link','https://pay.payos.vn/native','TEST','123456','SHOP','Native QR','CHO_THANH_TOAN');
                """);
            var hash=new PasswordHasher<TaiKhoanKhachHang>().HashPassword(new(),"Existing password 123!");
            await using(var set=new NpgsqlCommand("UPDATE public.khachhang SET matkhau=@hash WHERE makh='KH-OLD'",sql)){set.Parameters.AddWithValue("hash",hash);await set.ExecuteNonQueryAsync();}
            async Task<Dictionary<string,string>> Checksums()
            {
                var values=new Dictionary<string,string>();
                foreach(var t in nativeTables){var name=t.GetProperty("name").GetString()!;values[name]=await Scalar<string>("SELECT md5(COALESCE(string_agg(to_jsonb(t)::text,',' ORDER BY to_jsonb(t)::text),'')) FROM (SELECT "+string.Join(',',t.GetProperty("columns").EnumerateArray().Select(col=>Q(col.GetProperty("name").GetString()!)))+" FROM public."+Q(name)+") t");}
                return values;
            }
            var originals=await Checksums();
            var migrationPath=Path.Combine(AppContext.BaseDirectory,"Supabase-BoSungCot-ChucNang.sql");
            var contractPath=Path.Combine(AppContext.BaseDirectory,"Supabase_public_metadata_20261103.json");
            var reportPath=Path.Combine(Path.GetTempPath(),database+"-columns.json");
            await FashionStore.SchemaSetup.ColumnSchemaInstaller.ApplyAsync(settings.ConnectionString,migrationPath,contractPath,reportPath,true);
            await FashionStore.SchemaSetup.ColumnSchemaInstaller.ApplyAsync(settings.ConnectionString,migrationPath,contractPath,reportPath,true);
            Assert.Equal(originals.OrderBy(p=>p.Key),(await Checksums()).OrderBy(p=>p.Key));
            Assert.Equal(19,FashionStore.SchemaSetup.ColumnSchemaInstaller.Added.Sum(p=>p.Value.Count));
            var tamperPath=Path.Combine(Path.GetTempPath(),database+"-tamper.sql");
            await File.WriteAllTextAsync(tamperPath,(await File.ReadAllTextAsync(migrationPath))+"\nUPDATE public.sanpham SET dongia=1;");
            await Assert.ThrowsAsync<InvalidOperationException>(()=>FashionStore.SchemaSetup.ColumnSchemaInstaller.ApplyAsync(settings.ConnectionString,tamperPath,contractPath,reportPath,true));
            Assert.Equal(originals.OrderBy(p=>p.Key),(await Checksums()).OrderBy(p=>p.Key));
            await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{builder.UseEnvironment("Development");builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Database:Provider"]="Supabase",["Database:EnableSupabaseCustomerFeatures"]="true",["Database:EnableSupabaseOrders"]="true",["Orders:EnableBankTransfer"]="true",["Orders:EmployeeId"]="NV01",["ConnectionStrings:Supabase"]=settings.ConnectionString}));});
            using var client=factory.CreateClient();
            var login=await client.PostAsJsonAsync("/api/account/login",new LoginRequest{UserName="EXISTING",Password="Existing password 123!"});Assert.True(login.IsSuccessStatusCode,await login.Content.ReadAsStringAsync());client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",(await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
            var products=await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NAM");Assert.Equal(200000,Assert.Single(products!.Items).Price);
            var request=new CheckoutRequest("Khach thu","0900000001","Dia chi giao hang hop le","COD","Ghi chu goc",Guid.NewGuid(),Items:new[]{new CartItemRequest{Sku="SKU-M",Quantity=1}});
            var place=await client.PostAsJsonAsync("/api/orders",request);Assert.True(place.IsSuccessStatusCode,await place.Content.ReadAsStringAsync());var placed=(await place.Content.ReadFromJsonAsync<PlaceOrderResponse>())!;
            var retry=await client.PostAsJsonAsync("/api/orders",request);Assert.True(retry.IsSuccessStatusCode,await retry.Content.ReadAsStringAsync());Assert.Equal(placed.OrderId,(await retry.Content.ReadFromJsonAsync<PlaceOrderResponse>())!.OrderId);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/orders",request with {Recipient="Khach khac"})).StatusCode);
            var order=(await client.GetFromJsonAsync<OrderDetails>("/api/orders/"+placed.OrderId))!;Assert.True(order.CanUpdateDelivery);Assert.True(order.CanCancel);Assert.Equal("P1",Assert.Single(order.Items).ProductId);
            var update=await client.PutAsJsonAsync("/api/orders/"+placed.OrderId+"/information",new UpdateOrderRequest("delivery",order.EditVersion!,"Ten moi","0900000001","Dia chi da duoc cap nhat"));Assert.True(update.IsSuccessStatusCode,await update.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync("/api/orders/"+placed.OrderId+"/information",new UpdateOrderRequest("delivery",order.EditVersion!,"Ten cu","0900000001","Dia chi cu khong ghi de"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/orders/"+placed.OrderId+"/cancel",new CancelOrderRequest("Khong con nhu cau"))).StatusCode);
            var cancelled=(await client.GetFromJsonAsync<OrderDetails>("/api/orders/"+placed.OrderId))!; Assert.Equal("HUY",cancelled.Status); Assert.Equal("Ghi chu goc",cancelled.Note); Assert.Equal("Khong con nhu cau",cancelled.CancellationReason); Assert.NotNull(cancelled.CancelledAt);
            Assert.Equal(2,(await client.GetFromJsonAsync<List<OrderSummary>>("/api/orders"))!.Count);
            Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/customer/reviews/P1")).StatusCode);Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.PostAsJsonAsync("/api/account/recovery",new{Email="test@example.test"})).StatusCode);
            var register=await client.PostAsJsonAsync("/api/account/register",new RegisterRequest{UserName="newcustomer",Password="New password 123!",FullName="New customer",Phone="0900000003"});Assert.True(register.IsSuccessStatusCode,await register.Content.ReadAsStringAsync());
            await Exec("UPDATE public.hoadon SET trangthaigiaohang='Hàng giao thành công',trangthaithanhtoan='DA_THANH_TOAN',ngaygiaothanhcong=LOCALTIMESTAMP WHERE sohd='WEB-HISTORY'");
            var ret=await client.PostAsJsonAsync("/api/customer/returns",new{OrderId="WEB-HISTORY",Sku="SKU-M",Quantity=1,Kind="TRA",Reason="Ly do tra hang",RequestId=Guid.NewGuid()});Assert.True(ret.IsSuccessStatusCode,await ret.Content.ReadAsStringAsync());
            Assert.Equal(1L,await Scalar<long>("SELECT count(*) FROM public.phieudoitra"));Assert.Equal(18L,await Scalar<long>("SELECT count(*) FROM pg_tables WHERE schemaname='public'"));

            // Two SKUs of the same product in one invoice still allow only one review.
            await Exec("""
                INSERT INTO public.sanpham(masp,masku,maloai,tensanpham,kichco,mausac,dongia,soluong,soluongtonkho,danhmuc)
                VALUES('P1','SKU-L','AO','Ao thu L','L','Đen',250000,10,10,'NAM');
                INSERT INTO public.chitiethoadon(sohd,masku,soluong,dongia,thanhtien,giatrithuctra)
                VALUES('WEB-HISTORY','SKU-L',1,250000,250000,200000);
                UPDATE public.chitiethoadon SET giatrithuctra=200000 WHERE sohd='WEB-HISTORY' AND masku='SKU-M';
                UPDATE public.hoadon SET tongcong=500000,khachphaitra=400000 WHERE sohd='WEB-HISTORY';
                UPDATE public.sanpham SET tensanpham='Ten moi sau mua',kichco='XXL',mausac='Mau moi',hinhanh='https://example.test/new.png' WHERE masku='SKU-M';
                """);
            var snapshot=(await client.GetFromJsonAsync<OrderDetails>("/api/orders/"+placed.OrderId))!;
            Assert.Equal("Áo thun nam",Assert.Single(snapshot.Items).Name);
            Assert.Equal("M",snapshot.Items[0].Size);Assert.Equal("Đen",snapshot.Items[0].Color);
            var review=new ReviewInput{ProductId="P1",OrderId="WEB-HISTORY",Stars=5,Body="Nhan xet thu nghiem"};
            using(var bob=factory.CreateClient()){
                var bobLogin=await bob.PostAsJsonAsync("/api/account/login",new LoginRequest{UserName="newcustomer",Password="New password 123!"});
                bob.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",(await bobLogin.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
                Assert.Equal(HttpStatusCode.Conflict,(await bob.PostAsJsonAsync("/api/customer/reviews",review)).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict,(await bob.PostAsJsonAsync("/api/orders/WEB-HISTORY/refund-account",new CancelOrderRequest(AccountHolder:"BOB",AccountNumber:"12345678",BankName:"Test bank"))).StatusCode);
            }
            foreach(var state in new[]{("Chờ xác nhận","DA_THANH_TOAN"),("Hàng giao thành công","CHUA_THANH_TOAN")})
            {
                await using(var setState=new NpgsqlCommand("UPDATE public.hoadon SET trangthaigiaohang=@s,trangthaithanhtoan=@p WHERE sohd='WEB-HISTORY'",sql)){
                    setState.Parameters.AddWithValue("s",state.Item1);setState.Parameters.AddWithValue("p",state.Item2);await setState.ExecuteNonQueryAsync();
                }
                Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/customer/reviews",review)).StatusCode);
            }
            await Exec("UPDATE public.hoadon SET trangthaigiaohang='Hàng giao thành công',trangthaithanhtoan='DA_THANH_TOAN' WHERE sohd='WEB-HISTORY'");
            var eligible=await client.GetFromJsonAsync<List<Dictionary<string,JsonElement>>>("/api/customer/reviews/P1/eligible-orders");Assert.Single(eligible!);
            var race=await Task.WhenAll(client.PostAsJsonAsync("/api/customer/reviews",review),client.PostAsJsonAsync("/api/customer/reviews",review));
            Assert.Single(race,r=>r.StatusCode==HttpStatusCode.NoContent);Assert.Single(race,r=>r.StatusCode==HttpStatusCode.Conflict);foreach(var response in race)response.Dispose();
            Assert.Empty((await client.GetFromJsonAsync<List<Dictionary<string,JsonElement>>>("/api/customer/reviews/P1/eligible-orders"))!);
            using(var anonymous=factory.CreateClient()){
                var reviews=(await anonymous.GetFromJsonAsync<List<Dictionary<string,JsonElement>>>("/api/customer/reviews/P1"))!;
                Assert.Equal(review.Body,Assert.Single(reviews)["NoiDung"].GetString());Assert.DoesNotContain("SoHD",reviews[0].Keys);
                Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/customer/reviews/P1/mine")).StatusCode);
            }
            var otherSku=await Scalar<string>("SELECT masku FROM public.chitiethoadon WHERE sohd='WEB-HISTORY' AND sosao IS NULL LIMIT 1");
            await using(var duplicate=new NpgsqlCommand("UPDATE public.chitiethoadon SET sosao=4,noidungdanhgia='Lap',ngaydanhgia=CURRENT_TIMESTAMP,trangthaidanhgia='HIEN_THI' WHERE sohd='WEB-HISTORY' AND masku=@sku",sql)){
                duplicate.Parameters.AddWithValue("sku",otherSku);var violation=await Assert.ThrowsAsync<PostgresException>(()=>duplicate.ExecuteNonQueryAsync());Assert.Equal("23505",violation.SqlState);
            }
            // Refund details are kept on the existing return slip; customer cannot confirm a transfer.
            var account=await client.PostAsJsonAsync("/api/orders/WEB-HISTORY/refund-account",new CancelOrderRequest(AccountHolder:"KHACH THU",AccountNumber:"1234567890",BankName:"Test bank"));
            Assert.True(account.IsSuccessStatusCode,await account.Content.ReadAsStringAsync());
            await Exec("UPDATE public.phieudoitra SET sotienhoan=200000,manv='NV01',trangthai='DA_DUYET' WHERE sohd='WEB-HISTORY'");
            var pending=(await client.GetFromJsonAsync<OrderDetails>("/api/orders/WEB-HISTORY"))!;
            Assert.Equal("CHO_HOAN_TIEN",pending.Refund!.Status);Assert.Equal("•••• 7890",pending.Refund.MaskedAccountNumber);Assert.Null(pending.Refund.RefundedAt);
            await Exec("UPDATE public.phieudoitra SET trangthai='HOAN_TAT',mathamchieuhoantien='THU-001',ngayhoantien=CURRENT_TIMESTAMP WHERE sohd='WEB-HISTORY'");
            Assert.Equal("DA_HOAN_TIEN",(await client.GetFromJsonAsync<OrderDetails>("/api/orders/WEB-HISTORY"))!.Refund!.Status);

            // Test recovery against a closed localhost SMTP port: no real email is sent.
            await Exec("UPDATE public.khachhang SET email='existing@example.test' WHERE makh='KH-OLD'");
            var smtpConfig=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
                ["Recovery:SmtpHost"]="127.0.0.1",["Recovery:SmtpPort"]="15440",["Recovery:From"]="shop@example.test",["Recovery:ResetUrl"]="https://example.test/reset"}).Build();
            var mode=new DatabaseMode(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:Supabase"]=settings.ConnectionString}).Build());
            var recovery=new SupabasePasswordRecoveryService(new SupabaseCustomerFeaturesService(mode),smtpConfig,new PasswordHasher<TaiKhoanKhachHang>(),Microsoft.Extensions.Logging.Abstractions.NullLogger<SupabasePasswordRecoveryService>.Instance);
            await recovery.Request("unknown@example.test",default);Assert.Equal(0L,await Scalar<long>("SELECT count(*) FROM public.khachhang WHERE makhoiphucbam IS NOT NULL"));
            await recovery.Request("existing@example.test",default);
            Assert.True(await Scalar<bool>("SELECT length(makhoiphucbam)=64 AND length(phienmatkhaukhoiphuc)=64 AND hethanmakhoiphuc>CURRENT_TIMESTAMP FROM public.khachhang WHERE makh='KH-OLD'"));
            static string H(string value)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
            var passwordBefore=await Scalar<string>("SELECT matkhau FROM public.khachhang WHERE makh='KH-OLD'");
            async Task Token(string token,DateTime expires,string email){
                await using var cmd=new NpgsqlCommand("UPDATE public.khachhang SET makhoiphucbam=@token,hethanmakhoiphuc=@expires,phienmatkhaukhoiphuc=@version WHERE makh='KH-OLD'",sql);
                cmd.Parameters.AddWithValue("token",H(token));cmd.Parameters.AddWithValue("expires",expires);cmd.Parameters.AddWithValue("version",H(passwordBefore+"\n"+email));await cmd.ExecuteNonQueryAsync();
            }
            var expiredToken=new string('E',43);await Token(expiredToken,DateTime.UtcNow.AddMinutes(-1),"existing@example.test");
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/account/recovery/reset",new {Token=expiredToken,Password="Changed password 456!"})).StatusCode);
            var oldEmailToken=new string('B',43);await Token(oldEmailToken,DateTime.UtcNow.AddMinutes(20),"existing@example.test");
            await Exec("UPDATE public.khachhang SET email='changed@example.test' WHERE makh='KH-OLD'");
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/account/recovery/reset",new {Token=oldEmailToken,Password="Changed password 456!"})).StatusCode);
            var validToken=new string('A',43);await Token(validToken,DateTime.UtcNow.AddMinutes(20),"changed@example.test");
            var resets=await Task.WhenAll(client.PostAsJsonAsync("/api/account/recovery/reset",new {Token=validToken,Password="Changed password 456!"}),client.PostAsJsonAsync("/api/account/recovery/reset",new {Token=validToken,Password="Changed password 456!"}));
            Assert.Single(resets,r=>r.StatusCode==HttpStatusCode.NoContent);Assert.Single(resets,r=>r.StatusCode==HttpStatusCode.BadRequest);foreach(var response in resets)response.Dispose();
            Assert.True(await Scalar<bool>("SELECT makhoiphucbam IS NULL AND hethanmakhoiphuc IS NULL AND phienmatkhaukhoiphuc IS NULL FROM public.khachhang WHERE makh='KH-OLD'"));
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/account/recovery/reset",new {Token=validToken,Password="Changed password 456!"})).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/account/me")).StatusCode);
            var renewed=await client.PostAsJsonAsync("/api/account/login",new LoginRequest{UserName="existing",Password="Changed password 456!"});Assert.True(renewed.IsSuccessStatusCode,await renewed.Content.ReadAsStringAsync());
            Assert.Equal(18L,await Scalar<long>("SELECT count(*) FROM pg_tables WHERE schemaname='public'"));
        }
        finally{NpgsqlConnection.ClearAllPools();await using var drop=new NpgsqlCommand("DROP DATABASE "+database+" WITH(FORCE)",admin);await drop.ExecuteNonQueryAsync();}
    }
}
