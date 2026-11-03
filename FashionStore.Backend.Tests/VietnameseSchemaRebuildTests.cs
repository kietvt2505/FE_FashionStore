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

public sealed class VietnameseSchemaRebuildTests
{
    [PostgresLocalFact]
    public async Task RebuildOnCurrentPartnerStructureReusesNativeDataAndRestoresWebsite()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FASHIONSTORE_POSTGRES_TEST_CONNECTION")!);
        Assert.Contains(settings.Host,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("fashionstore_test",settings.Database);
        var database="fashionstore_vietnamese_"+Guid.NewGuid().ToString("N");settings.Database="postgres";
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
                foreach(var t in nativeTables){var name=t.GetProperty("name").GetString()!;values[name]=await Scalar<string>("SELECT md5(COALESCE(string_agg(to_jsonb(t)::text,',' ORDER BY to_jsonb(t)::text),'')) FROM public."+Q(name)+" t");}
                return values;
            }
            var before=await Checksums();var install=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Website-TenViet.sql"));
            var installPath=Path.Combine(AppContext.BaseDirectory,"Supabase-Website-TenViet.sql");
            var contractPath=Path.Combine(AppContext.BaseDirectory,"Supabase_public_metadata_20261103.json");
            var reportPath=Path.Combine(Path.GetTempPath(),database+".json");
            await FashionStore.SchemaSetup.SchemaInstaller.ApplyAsync(settings.ConnectionString,installPath,contractPath,reportPath,true);
            await Exec(install);
            var alteredInstall=Path.Combine(Path.GetTempPath(),database+".sql");
            await File.WriteAllTextAsync(alteredInstall,install+"\nUPDATE public.sanpham SET mota='unexpected-change' WHERE masku='SKU-M';");
            await Assert.ThrowsAsync<InvalidOperationException>(()=>FashionStore.SchemaSetup.SchemaInstaller.ApplyAsync(settings.ConnectionString,alteredInstall,contractPath,reportPath,true));
            Assert.Equal(before.OrderBy(p=>p.Key), (await Checksums()).OrderBy(p=>p.Key));
            Assert.Equal(35L,await Scalar<long>("SELECT count(*) FROM pg_tables WHERE schemaname='public'"));
            Assert.False(await Scalar<bool>("SELECT to_regclass('public.customer_account') IS NOT NULL OR to_regclass('public.cart') IS NOT NULL OR to_regclass('public.order_snapshot') IS NOT NULL"));
            Assert.Equal(1L,await Scalar<long>("SELECT count(*) FROM public.thongtindonhang WHERE sohd='WEB-HISTORY'"));
            Assert.Equal(250000m,await Scalar<decimal>("SELECT price FROM public.chitietdonhanglucmua WHERE sohd='WEB-HISTORY'"));
            Assert.Equal(200000m,await Scalar<decimal>("SELECT sale_price FROM public.giabanthuctesanpham WHERE masku='SKU-M'"));
            await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{
                builder.UseEnvironment("Development");builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
                    ["Database:Provider"]="Supabase",["Database:EnableSupabaseCustomerFeatures"]="true",["Database:EnableSupabaseOrders"]="true",
                    ["Orders:EnableBankTransfer"]="true",["Orders:EmployeeId"]="NV01",["ConnectionStrings:Supabase"]=settings.ConnectionString,
                    ["Recovery:SmtpHost"]="127.0.0.1",["Recovery:SmtpPort"]="1",["Recovery:From"]="shop@example.test",["Recovery:ResetUrl"]="https://example.test/reset"}));
            });
            using var client=factory.CreateClient();
            Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/account/recovery",new {Email="pos@example.test"})).StatusCode);
            Assert.Equal(0L,await Scalar<long>("SELECT count(*) FROM public.yeucaukhoiphucmatkhau"));
            using(var newcomer=factory.CreateClient())
            {
                var registration=await newcomer.PostAsJsonAsync("/api/account/register",new RegisterRequest{UserName="newaccount",Password="New password 123!",FullName="Khach web moi",Phone="0900000003"});
                Assert.True(registration.StatusCode==HttpStatusCode.Created,await registration.Content.ReadAsStringAsync());
                var newCustomer=(await registration.Content.ReadFromJsonAsync<CustomerResponse>())!;
                Assert.Equal(1L,await Scalar<long>("SELECT count(*) FROM public.khachhang WHERE tendangnhap='newaccount' AND matkhau IS NOT NULL"));
                Assert.Equal(1L,await Scalar<long>("SELECT count(*) FROM public.giohang"));
                var newLogin=await newcomer.PostAsJsonAsync("/api/account/login",new LoginRequest{UserName="NEWACCOUNT",Password="New password 123!"});
                Assert.True(newLogin.IsSuccessStatusCode,await newLogin.Content.ReadAsStringAsync());
                newcomer.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",(await newLogin.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
                var profile=await newcomer.PutAsJsonAsync("/api/account/me",new ProfileRequest{FullName="Khach web cap nhat",Phone="0900000003",RowVersion=newCustomer.RowVersion});
                Assert.True(profile.IsSuccessStatusCode,await profile.Content.ReadAsStringAsync());
                Assert.Equal(HttpStatusCode.NoContent,(await newcomer.PostAsJsonAsync("/api/account/password",new ChangePasswordRequest{CurrentPassword="New password 123!",NewPassword="Changed password 123!"})).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized,(await newcomer.GetAsync("/api/account/me")).StatusCode);
                Assert.Equal(HttpStatusCode.OK,(await newcomer.PostAsJsonAsync("/api/account/login",new LoginRequest{UserName="newaccount",Password="Changed password 123!"})).StatusCode);
            }
            var login=await client.PostAsJsonAsync("/api/account/login",new LoginRequest{UserName="EXISTING",Password="Existing password 123!"});
            Assert.True(login.IsSuccessStatusCode,await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",(await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
            Assert.Single((await client.GetFromJsonAsync<List<OrderSummary>>("/api/orders"))!);
            var products=(await client.GetFromJsonAsync<ProductListResponse>("/api/products?audience=NAM"))!;
            Assert.Equal("P1",Assert.Single(products.Items).Id);Assert.Equal(200000,products.Items[0].Price);
            var detail=(await client.GetFromJsonAsync<ProductDetailResponse>("/api/products/P1"))!;
            Assert.Equal("Mô tả có sẵn",detail.Description);Assert.Contains(detail.Images,p=>p.Url=="https://example.test/black.png");
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/cart/items",new CartItemRequest{Sku="SKU-M",Quantity=1})).StatusCode);
            Assert.Equal(200000,(await client.GetFromJsonAsync<CartResponse>("/api/cart"))!.Subtotal);
            var placed=await client.PostAsJsonAsync("/api/orders",new CheckoutRequest("Khach cu","0900000001","Dia chi giao hang moi","COD",null,Guid.NewGuid()));
            Assert.True(placed.IsSuccessStatusCode,await placed.Content.ReadAsStringAsync());
            var id=(await placed.Content.ReadFromJsonAsync<PlaceOrderResponse>())!.OrderId;
            Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/orders/"+id+"/cancel",new CancelOrderRequest("Khong con nhu cau mua"))).StatusCode);
            Assert.Equal("HUY",(await client.GetFromJsonAsync<OrderDetails>("/api/orders/"+id))!.Status);
            Assert.Equal(10,await Scalar<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='SKU-M'"));
            await Exec("INSERT INTO public.giaodichthanhtoanqr(magd,sohd,nguondon,sotien,malienket,duongdanthanhtoan,manganhang,sotaikhoan,tentaikhoan,noidung,trangthai) SELECT 2,'WEB-HISTORY','WEB',200000,'link2','https://pay.payos.vn/old','TEST','123456','SHOP','old','CHO_THANH_TOAN'");
            var existing=(await client.GetFromJsonAsync<OrderDetails>("/api/orders/WEB-HISTORY"))!;Assert.False(existing.CanUpdatePayment);
            Assert.Equal(2L,await Scalar<long>("SELECT count(*) FROM public.giaodichthanhtoanqr"));
        }
        finally{NpgsqlConnection.ClearAllPools();await using var drop=new NpgsqlCommand("DROP DATABASE "+database+" WITH(FORCE)",admin);await drop.ExecuteNonQueryAsync();}
    }
}
