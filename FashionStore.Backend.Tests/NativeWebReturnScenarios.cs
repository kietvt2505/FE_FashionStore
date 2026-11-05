using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class NativeWebReturnScenarios
{
    public static async Task RunAsync(string connection,WebApplicationFactory<Program> factory)
    {
        await using var c=new NpgsqlConnection(connection);await c.OpenAsync();
        async Task Exec(string sql){await using var cmd=new NpgsqlCommand(sql,c);await cmd.ExecuteNonQueryAsync();}
        async Task<T> Value<T>(string sql){await using var cmd=new NpgsqlCommand(sql,c);return (T)(await cmd.ExecuteScalarAsync())!;}
        await Exec(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-DoiTra-CungSanPham.sql")));
#if PARTNER_WINFORMS
        // This historical web metadata predates the form's existing inspection fields.
        foreach(var table in new[]{"chitiethoadon","chitietdoitra"})
            await Exec($"ALTER TABLE public.{table} ADD COLUMN IF NOT EXISTS trangthaikiemnhan varchar(30), ADD COLUMN IF NOT EXISTS ghichukiemnhan varchar(1000), ADD COLUMN IF NOT EXISTS manvkiemnhan varchar(20), ADD COLUMN IF NOT EXISTS ngaykiemnhan timestamp without time zone");
        await Exec(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Partner-KiemNhanDoiTra.sql")));
#endif
        await Exec("""
            INSERT INTO public.sanpham(masp,masku,maloai,tensanpham,kichco,mausac,dongia,soluong,soluongtonkho,danhmuc)
            VALUES('RET-TEE','RET-BLK-S','AO','Áo thun Nam','S','Đen',100000,20,20,'NAM'),
                  ('RET-TEE','RET-BLK-M','AO','Áo thun Nam','M','Đen',100000,20,20,'NAM'),
                  ('RET-TEE','RET-BLU-S','AO','Áo thun Nam','S','Xanh',100000,20,20,'NAM'),
                  ('RET-OTHER','RET-OTHER-S','AO','Sản phẩm khác','S','Đen',100000,20,20,'NAM');
            INSERT INTO public.hoadon(sohd,makh,nguondon,loaihoadon,phuongthucthanhtoan,trangthaigiaohang,trangthaithanhtoan,
                tongcong,thuevat,khachphaitra,tennguoinhan,sodienthoai_kh,diachigiao,manv,ngaygiaothanhcong,datrutonkho,daghinhanthanhtoan)
            SELECT id,'KH-POS','WEB','GIAO_HANG','CHUYEN_KHOAN','Hàng giao thành công','DA_THANH_TOAN',200000,16000,216000,
                'Khách đổi hàng','0900000002','Địa chỉ gốc','NV01',LOCALTIMESTAMP,TRUE,TRUE
            FROM (VALUES('WEB-RET-EQUAL'),('WEB-RET-MORE'),('WEB-RET-LESS'),('WEB-RET-REFUND')) AS ids(id);
            INSERT INTO public.chitiethoadon(sohd,masku,soluong,dongia,thanhtien,giatrithuctra)
            SELECT id,'RET-BLK-S',2,100000,200000,216000 FROM (VALUES('WEB-RET-EQUAL'),('WEB-RET-MORE'),('WEB-RET-LESS'),('WEB-RET-REFUND')) AS ids(id);
            """);
        using var client=factory.CreateClient();
        using(var login=await client.PostAsJsonAsync("/api/account/login",new LoginRequest{UserName="restored-pos",Password="Restored password 123!"}))
        {Assert.True(login.IsSuccessStatusCode,await login.Content.ReadAsStringAsync());client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",(await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);}
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Database:Provider"]="Supabase",["Database:EnableSupabaseCustomerFeatures"]="true",["Database:EnableSupabaseOrders"]="true",["ConnectionStrings:Supabase"]=connection}).Build();
        var features=new SupabaseCustomerFeaturesService(new(config));
        var options=(await client.GetFromJsonAsync<CustomerReturnOptions>("/api/customer/returns/options/WEB-RET-EQUAL"))!;
        var original=Assert.Single(options.Items);Assert.Equal("RET-BLK-S",original.Sku);Assert.Equal(2,original.PurchasedQuantity);Assert.Equal(2,original.RemainingQuantity);
        Assert.Equal("Khách đổi hàng",options.Recipient);Assert.Equal("Địa chỉ gốc",options.Address);
        Assert.Equal(3,original.Variants.Count);Assert.DoesNotContain(original.Variants,v=>v.Sku=="RET-OTHER-S");
        var privateError=await Assert.ThrowsAsync<FashionStore.Backend.Middleware.CustomerRequestException>(()=>features.ReturnOptions("KH-OLD","WEB-RET-EQUAL",default));Assert.Equal(404,privateError.Status);
        using(var anonymous=factory.CreateClient())Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/customer/returns/options/WEB-RET-EQUAL")).StatusCode);
        var forged=new ReturnInput{OrderId="WEB-RET-EQUAL",Sku="RET-BLK-S",Quantity=2,NewQuantity=2,Kind="DOI_SAN_PHAM",NewSku="RET-OTHER-S",Reason="Muốn đổi sản phẩm khác",RequestId=Guid.NewGuid()};
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/customer/returns",forged)).StatusCode);
        Assert.Equal(0L,await Value<long>("SELECT count(*) FROM public.phieudoitra WHERE sohd='WEB-RET-EQUAL'"));
        forged.NewSku="RET-BLK-M";forged.NewQuantity=21;
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/customer/returns",forged)).StatusCode);
        forged.NewQuantity=1;
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/customer/returns",forged)).StatusCode);
        forged.NewQuantity=3;forged.Quantity=3;
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/customer/returns",forged)).StatusCode);
        await Exec("UPDATE public.hoadon SET ngaygiaothanhcong=LOCALTIMESTAMP-INTERVAL '16 days' WHERE sohd='WEB-RET-EQUAL'");
        Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync("/api/customer/returns/options/WEB-RET-EQUAL")).StatusCode);
        await Exec("UPDATE public.hoadon SET ngaygiaothanhcong=LOCALTIMESTAMP WHERE sohd='WEB-RET-EQUAL'");
#if PARTNER_WINFORMS
        await Exec("INSERT INTO public.phanquyen(maquyen,tenquyen,trangthai) VALUES('CHT','Cửa hàng trưởng',true) ON CONFLICT(maquyen) DO NOTHING; UPDATE public.nhanvien SET maquyen='CHT' WHERE manv='NV01'");
        QLIShopThoiTrang.Api.Data.StoreRepository partner;
        var oldConnection=Environment.GetEnvironmentVariable("YODYSTORE_CONNECTION_STRING");
        try{Environment.SetEnvironmentVariable("YODYSTORE_CONNECTION_STRING",connection);partner=new(new(config));}finally{Environment.SetEnvironmentVariable("YODYSTORE_CONNECTION_STRING",oldConnection);}
#endif
        foreach(var (suffix,newQuantity,price,refund,extra) in new[]{("EQUAL",2,100000m,0m,0m),("MORE",2,150000m,0m,108000m),("LESS",2,50000m,108000m,0m),("REFUND",0,100000m,216000m,0m)})
        {
            await Exec("UPDATE public.sanpham SET dongia="+price.ToString(System.Globalization.CultureInfo.InvariantCulture)+" WHERE masku='RET-BLK-M'");
            var order="WEB-RET-"+suffix;
            var input=new ReturnInput{OrderId=order,Sku="RET-BLK-S",Quantity=2,Kind=newQuantity==0?"HOAN_TIEN":"DOI_SAN_PHAM",NewSku=newQuantity==0?null:"RET-BLK-M",NewQuantity=newQuantity==0?null:newQuantity,
                Recipient="Người nhận mới",Phone="0900000022",ExchangeAddress="Địa chỉ nhận hàng đổi mới",Reason="Đổi màu hoặc kích thước / trả hàng",RequestId=Guid.NewGuid()};
            var oldStock=await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='RET-BLK-S'");var newStock=await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='RET-BLK-M'");
            using(var response=await client.PostAsJsonAsync("/api/customer/returns",input))Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/customer/returns",input)).StatusCode);
            Assert.Equal(1L,await Value<long>("SELECT count(*) FROM public.phieudoitra WHERE sohd='"+order+"'"));
            Assert.Equal(oldStock,await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='RET-BLK-S'"));Assert.Equal(newStock,await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='RET-BLK-M'"));
            var id=await Value<string>("SELECT sophieudt FROM public.phieudoitra WHERE sohd='"+order+"'");
            Assert.Equal("Địa chỉ nhận hàng đổi mới",await Value<string>("SELECT diachidoihang FROM public.phieudoitra WHERE sophieudt='"+id+"'"));
            Assert.Equal("Địa chỉ gốc",await Value<string>("SELECT diachigiao FROM public.hoadon WHERE sohd='"+order+"'"));
            Assert.Equal(newQuantity,await Value<int>("SELECT soluongdoi FROM public.chitietdoitra WHERE sophieudt='"+id+"'"));
            Assert.Equal(0,(await client.GetFromJsonAsync<CustomerReturnOptions>("/api/customer/returns/options/"+order))!.Items[0].RemainingQuantity);
#if PARTNER_WINFORMS
            var pending=(await partner.GetWebReturnRequestsAsync()).Single(x=>x.Id==id);Assert.Equal("Người nhận mới",pending.Recipient);Assert.Equal("0900000022",pending.ExchangePhone);
            if(suffix=="EQUAL"){
                foreach(var forgedQuantity in new[]{1,3}){
                    await Exec("UPDATE public.chitietdoitra SET soluongdoi="+forgedQuantity+" WHERE sophieudt='"+id+"'");
                    var quantityError=await Assert.ThrowsAsync<InvalidOperationException>(()=>partner.ApproveWebReturnAsync(id,"NV01"));
                    Assert.Contains("Số lượng hàng đổi phải bằng",quantityError.Message);
                    Assert.Equal("CHO_DUYET",await Value<string>("SELECT trangthai FROM public.phieudoitra WHERE sophieudt='"+id+"'"));
                    Assert.Equal(newStock,await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='RET-BLK-M'"));
                }
                await Exec("UPDATE public.chitietdoitra SET soluongdoi=2 WHERE sophieudt='"+id+"'");
                // A forged/old request must still be checked by the staff approval backend.
                await Exec("UPDATE public.chitietdoitra SET maskudoi='RET-OTHER-S' WHERE sophieudt='"+id+"'");
                await Assert.ThrowsAsync<InvalidOperationException>(()=>partner.ApproveWebReturnAsync(id,"NV01"));
                Assert.Equal("CHO_DUYET",await Value<string>("SELECT trangthai FROM public.phieudoitra WHERE sophieudt='"+id+"'"));
                await Exec("UPDATE public.chitietdoitra SET maskudoi='RET-BLK-M' WHERE sophieudt='"+id+"';UPDATE public.sanpham SET soluongtonkho=0 WHERE masku='RET-BLK-M'");
                await Assert.ThrowsAsync<InvalidOperationException>(()=>partner.ApproveWebReturnAsync(id,"NV01"));
                Assert.Equal(oldStock,await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='RET-BLK-S'"));
                await Exec("UPDATE public.sanpham SET soluongtonkho="+newStock+" WHERE masku='RET-BLK-M'");
            }
            var receipt=await partner.ApproveWebReturnAsync(id,"NV01");await partner.ApproveWebReturnAsync(id,"NV01");
            Assert.Equal(refund,receipt.RefundAmount);Assert.Equal(extra,receipt.ExtraAmount);
            Assert.Equal(newStock-newQuantity,await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='RET-BLK-M'"));
            Assert.Equal(oldStock,await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='RET-BLK-S'"));
            if(refund>0){
                var bank=new CancelOrderRequest(AccountHolder:"NGUOI NHAN",AccountNumber:"1234567890",BankName:"NGAN HANG TEST");
                Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/customer/returns/"+id+"/refund-account",bank)).StatusCode);
                var wrongOwner=await Assert.ThrowsAsync<FashionStore.Backend.Middleware.CustomerRequestException>(()=>features.SaveReturnRefundAccount("KH-OLD",id,bank,default));Assert.Equal(404,wrongOwner.Status);
                Assert.Equal("•••• 7890",(await partner.GetWebReturnRequestsAsync()).Single(x=>x.Id==id).BankAccount);
                Assert.Equal("1234567890",(await partner.GetWebReturnRequestsAsync(true)).Single(x=>x.Id==id).BankAccount);
            }
            var inspect=new QLIShopThoiTrang.Api.Models.StockReviewActionDto(true,"Đã nhận một sản phẩm còn nguyên tem",true,1,1,Guid.NewGuid(),0);
            await partner.ReviewReturnedStockAsync("DOI_TRA",id,"RET-BLK-S",inspect,"NV01");await partner.ReviewReturnedStockAsync("DOI_TRA",id,"RET-BLK-S",inspect,"NV01");
            Assert.Equal(oldStock+1,await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='RET-BLK-S'"));
            Assert.Equal(refund,await Value<decimal>("SELECT sotienhoan FROM public.phieudoitra WHERE sophieudt='"+id+"'"));
            var inspected=(await partner.GetStockReviewsAsync("DOI_TRA")).Single(x=>x.DocumentId==id);Assert.Equal("DANG_KIEM",inspected.Status);Assert.Equal(1,inspected.ReceivedQty);
#endif
        }
        var list=await client.GetStringAsync("/api/customer/returns");Assert.Contains("WEB-RET-REFUND",list);Assert.DoesNotContain("1234567890",list);
        Assert.Equal(18L,await Value<long>("SELECT count(*) FROM pg_tables WHERE schemaname='public'"));
    }
}
