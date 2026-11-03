using System.Net;
using System.Net.Http.Json;
using FashionStore.Backend.Data;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Middleware;
using FashionStore.Backend.Services;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class OrderInformationIntegrationTests
{
    [Fact]
    public async Task AnonymousCannotUpdateAnOrder()
    {
        await using var factory=new ApiFactory();using var client=factory.CreateClient();
        using var response=await client.PutAsJsonAsync("/api/orders/HD-1/information",
            new UpdateOrderRequest("payment",new string('A',64),PaymentMethod:"COD"));
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    [PostgresLocalFact]
    public async Task UpdatesKeepTheOrderAndBalancesAndSerializeWithPayments()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FASHIONSTORE_POSTGRES_TEST_CONNECTION")!);
        Assert.Contains(settings.Host,new[]{"127.0.0.1","localhost","::1"});Assert.Equal("fashionstore_test",settings.Database);
        var database="fashionstore_order_update_"+Guid.NewGuid().ToString("N");settings.Database="postgres";
        await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        await using(var create=new NpgsqlCommand("CREATE DATABASE "+database,admin))await create.ExecuteNonQueryAsync();
        try
        {
            settings.Database=database;settings.Timezone="UTC";
            await using var sql=new NpgsqlConnection(settings.ConnectionString);await sql.OpenAsync();
            async Task Exec(string command){await using var cmd=new NpgsqlCommand(command,sql);await cmd.ExecuteNonQueryAsync();}
            async Task<T> Scalar<T>(string command){await using var cmd=new NpgsqlCommand(command,sql);return (T)(await cmd.ExecuteScalarAsync())!;}
            await Exec("""
                CREATE TABLE public.hoadon(sohd varchar(30) PRIMARY KEY,makh varchar(20),loaihoadon text,nguondon text,
                    phuongthucthanhtoan text,trangthaigiaohang text,trangthaithanhtoan text,khachphaitra numeric(18,2),
                    tienkhachdua numeric(18,2),ngaygiaothanhcong timestamp,tennguoinhan text,sodienthoai_kh text,diachigiao text,tiencod numeric(18,2));
                CREATE TABLE public.thongtindonhang(sohd varchar(30) PRIMARY KEY,makh varchar(20),ngaylap timestamp,
                    recipient text,phone text,address text,note text,total numeric(18,2),paymentmethod text);
                CREATE TABLE public.chitietdonhanglucmua(sohd varchar(30),masku text,name text,size text,color text,quantity int,price numeric(18,2));
                CREATE TABLE public.sanpham(masku text,masp text,mausac text,hinhanh text,soluongtonkho int);
                CREATE TABLE public.yeucauthanhtoantructuyen(id text,sohd varchar(30));
                INSERT INTO public.hoadon VALUES('HD-1','ALICE','COD','WEB','COD','Chờ xác nhận','CHUA_THANH_TOAN',498000,0,NULL,'Alice','0900000001','Địa chỉ trước cập nhật',498000);
                INSERT INTO public.thongtindonhang VALUES('HD-1','ALICE',CURRENT_TIMESTAMP,'Alice','0900000001','Địa chỉ trước cập nhật','Ghi chú ban đầu',498000,'COD');
                INSERT INTO public.chitietdonhanglucmua VALUES('HD-1','SKU-M','Áo thun trơn nam','M','Đen',2,249000);
                INSERT INTO public.sanpham VALUES('SKU-M','SP-002','Đen',NULL,20);
                """);
            DatabaseMode Mode(bool bank=true)=>new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
                ["Database:Provider"]="Supabase",["Database:EnableSupabaseOrders"]="true",["Database:EnableSupabaseCustomerFeatures"]="true",
                ["Orders:EnableBankTransfer"]=bank.ToString(),["ConnectionStrings:Supabase"]=settings.ConnectionString}).Build());
            var service=new SupabaseOrderService(Mode());
            async Task<OrderDetails> Read()=>await service.DetailAsync("HD-1","ALICE",default);
            async Task Reject(string customer,UpdateOrderRequest request,int status)
            {
                var error=await Assert.ThrowsAsync<CustomerRequestException>(()=>service.UpdateInformationAsync(customer,"HD-1",request,default));
                Assert.Equal(status,error.Status);
            }
            var initial=await Read();Assert.True(initial.CanUpdateDelivery);Assert.True(initial.CanUpdatePayment);
            var delivery=new UpdateOrderRequest("delivery",initial.EditVersion!,"Nguyễn An","0912345678","123 đường mới, Phường Bắc Giang");
            await Reject("BOB",delivery,404);
            await Reject("ALICE",delivery with {Phone="not-a-phone"},400);
            await service.UpdateInformationAsync("ALICE","HD-1",delivery,default);
            var updated=await Read();Assert.Equal(delivery.Address,updated.Address);Assert.Equal(delivery.Recipient,updated.Recipient);
            Assert.Equal(delivery.Address,await Scalar<string>("SELECT diachigiao FROM public.hoadon"));
            Assert.Equal(delivery.Phone,await Scalar<string>("SELECT sodienthoai_kh FROM public.hoadon"));
            Assert.Equal(initial.Total,updated.Total);Assert.Equal(initial.Items,updated.Items);Assert.Equal(initial.Note,updated.Note);
            await Reject("ALICE",delivery,409); // Stale form cannot overwrite a newer edit.

            var payment=new UpdateOrderRequest("payment",updated.EditVersion!,PaymentMethod:"CHUYEN_KHOAN");
            await service.UpdateInformationAsync("ALICE","HD-1",payment,default);
            var transfer=await Read();Assert.Equal("CHUYEN_KHOAN",transfer.PaymentMethod);Assert.Equal("Chờ xác nhận thanh toán",transfer.ShippingStatus);
            Assert.Equal("WEB_CHO_THANH_TOAN",await Scalar<string>("SELECT loaihoadon FROM public.hoadon"));
            Assert.Equal(0m,await Scalar<decimal>("SELECT tiencod FROM public.hoadon"));
            await service.UpdateInformationAsync("ALICE","HD-1",new("payment",transfer.EditVersion!,PaymentMethod:"COD"),default);
            var cod=await Read();Assert.Equal("COD",cod.PaymentMethod);Assert.Equal(initial.Total,await Scalar<decimal>("SELECT tiencod FROM public.hoadon"));
            var disabledBank=new SupabaseOrderService(Mode(false));
            var unavailable=await Assert.ThrowsAsync<CustomerRequestException>(()=>disabledBank.UpdateInformationAsync("ALICE","HD-1",new("payment",cod.EditVersion!,PaymentMethod:"CHUYEN_KHOAN"),default));
            Assert.Equal(409,unavailable.Status);
            await Reject("ALICE",new("payment",cod.EditVersion!,PaymentMethod:"VNPAY"),409);

            foreach(var shipping in new[]{"Đang chuẩn bị","Chờ lấy hàng","Shipper đã lấy hàng","Hàng giao thành công","UNKNOWN"})
            {
                await Exec($"UPDATE public.hoadon SET trangthaigiaohang='{shipping}'");
                var blocked=await Read();Assert.False(blocked.CanUpdateDelivery);Assert.False(blocked.CanUpdatePayment);
                await Reject("ALICE",delivery with {Version=blocked.EditVersion!},409);
            }
            await Exec("UPDATE public.hoadon SET trangthaigiaohang='Vừa xác nhận đơn',loaihoadon='GIAO_HANG',trangthaithanhtoan='DA_THANH_TOAN',tienkhachdua=khachphaitra");
            var paid=await Read();Assert.True(paid.CanUpdateDelivery);Assert.False(paid.CanUpdatePayment);
            await Reject("ALICE",new("payment",paid.EditVersion!,PaymentMethod:"CHUYEN_KHOAN"),409);
            await service.UpdateInformationAsync("ALICE","HD-1",delivery with {Version=paid.EditVersion!,Address="456 đường nhận hàng mới"},default);
            Assert.Equal("DA_THANH_TOAN",(await Read()).PaymentStatus);
            await Exec("UPDATE public.hoadon SET loaihoadon='WEB_HUY',trangthaigiaohang='Đã hủy'");
            Assert.False((await Read()).CanUpdateDelivery);
            await Exec("UPDATE public.hoadon SET loaihoadon='WEB_CHO_THANH_TOAN',trangthaigiaohang='Chờ xác nhận thanh toán',trangthaithanhtoan='CHUA_THANH_TOAN',tienkhachdua=0,phuongthucthanhtoan='VNPAY'; UPDATE public.thongtindonhang SET paymentmethod='VNPAY'");
            var beforeAttempt=await Read();Assert.True(beforeAttempt.CanUpdatePayment);
            // Mimic gateway Start: it owns the invoice lock and inserts an attempt without updating the invoice row.
            await using(var paymentConnection=new NpgsqlConnection(settings.ConnectionString))
            {
                await paymentConnection.OpenAsync();await using var paymentTx=await paymentConnection.BeginTransactionAsync();
                await using(var lockInvoice=new NpgsqlCommand("SELECT sohd FROM public.hoadon WHERE sohd='HD-1' FOR UPDATE",paymentConnection,paymentTx))await lockInvoice.ExecuteScalarAsync();
                await using(var insert=new NpgsqlCommand("INSERT INTO public.yeucauthanhtoantructuyen VALUES('attempt-1','HD-1')",paymentConnection,paymentTx))await insert.ExecuteNonQueryAsync();
                var changing=service.UpdateInformationAsync("ALICE","HD-1",new("payment",beforeAttempt.EditVersion!,PaymentMethod:"COD"),default);
                await Task.Delay(100);Assert.False(changing.IsCompleted);
                await paymentTx.CommitAsync();
                var rejected=await Assert.ThrowsAsync<CustomerRequestException>(()=>changing);Assert.Equal(409,rejected.Status);
            }
            var withAttempt=await Read();Assert.False(withAttempt.CanUpdatePayment);Assert.True(withAttempt.CanUpdateDelivery);
            Assert.Contains("giao dịch",withAttempt.PaymentUpdateNotice);
            Assert.Equal("VNPAY",withAttempt.PaymentMethod);
            Assert.Equal(1L,await Scalar<long>("SELECT count(*) FROM public.hoadon"));
            Assert.Equal(1L,await Scalar<long>("SELECT count(*) FROM public.thongtindonhang"));
            Assert.Equal(20,await Scalar<int>("SELECT soluongtonkho FROM public.sanpham"));
            Assert.Equal(2,await Scalar<int>("SELECT quantity FROM public.chitietdonhanglucmua"));
            Assert.Equal(498000m,await Scalar<decimal>("SELECT total FROM public.thongtindonhang"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop=new NpgsqlCommand("DROP DATABASE "+database+" WITH (FORCE)",admin);await drop.ExecuteNonQueryAsync();
        }
    }
}
