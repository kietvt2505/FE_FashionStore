using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FashionStore.Backend.Data;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Middleware;
using FashionStore.Backend.Services;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class NativeStoreParityScenarios
{
    public static async Task RunAsync(string connection)
    {
        var settings=new NpgsqlConnectionStringBuilder(connection);
        Assert.StartsWith("fashionstore_native18_",settings.Database!);Assert.Contains(settings.Host,new[]{"127.0.0.1","localhost","::1"});
        await using var c=new NpgsqlConnection(connection);await c.OpenAsync();
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,c);await cmd.ExecuteNonQueryAsync();}
        async Task<T> Value<T>(string text){await using var cmd=new NpgsqlCommand(text,c);return (T)(await cmd.ExecuteScalarAsync())!;}
        await Exec("""
            INSERT INTO public.khachhang(makh,hoten,sodienthoai,tongchitieu) VALUES('KH-PRICE','Pricing customer','0900000010',0);
            INSERT INTO public.sanpham(masp,masku,maloai,tensanpham,kichco,mausac,dongia,soluong,soluongtonkho,danhmuc)
            VALUES('PRICE','PRICE-A','AO','Pricing A','M','Den',1000000,100,100,'NAM'),('PRICE-B','PRICE-B','AO','Pricing B','L','Den',666667,100,100,'NAM');
            INSERT INTO public.khuyenmai(makm,tenchuongtrinh,loaikm,giatrigiam,ngaybatdau,ngayketthuc,trangthai,giatridonhangtoithieu)
            SELECT code,code,kind,amount,(CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh')-INTERVAL '1 day',
                (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh')+INTERVAL '1 day',true,0
            FROM (VALUES('PRICE-LOW','SO_TIEN',50000),('PRICE-TIE','SO_TIEN',100000),('PRICE-HIGH','SO_TIEN',150000),('PRICE-PCT','PHAN_TRAM',20)) AS v(code,kind,amount);
            """);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["Database:Provider"]="Supabase",["Database:EnableSupabaseCustomerFeatures"]="true",["Database:EnableSupabaseOrders"]="true",
            ["Orders:EnableBankTransfer"]="true",["Orders:EmployeeId"]="NV01",["ConnectionStrings:Supabase"]=connection}).Build();
        var mode=new DatabaseMode(config);var orders=new SupabaseOrderService(mode);var features=new SupabaseCustomerFeaturesService(mode);
        var items=new[]{new CartItemRequest{Sku="PRICE-A",Quantity=1}};
        async Task<CheckoutQuote> Quote(string? voucher=null)=>await orders.QuoteBrowserAsync("KH-PRICE",items,voucher,default);
        foreach(var (spent,rank,rate,total) in new[]{(0m,"Bạc",0,1080000m),(5000000m,"Bạc",0,1080000m),(5000001m,"Vàng",5,1026000m),(15000000m,"Vàng",5,1026000m),(15000001m,"Kim Cương",10,972000m)})
        {
            await using(var set=new NpgsqlCommand("UPDATE public.khachhang SET tongchitieu=@spent WHERE makh='KH-PRICE'",c)){set.Parameters.AddWithValue("spent",spent);await set.ExecuteNonQueryAsync();}
            var q=await Quote();Assert.Equal(rank,q.MemberRank);Assert.Equal(rate,q.MemberRate);Assert.Equal(total,q.Total);Assert.Equal(8,q.VatRate);
#if PARTNER_WINFORMS
            await AssertPartnerQuote(c,q,null);
#endif
        }
        foreach(var (code,discount,member,winner,total) in new[]{("PRICE-LOW",100000m,100000m,(string?)null,972000m),("PRICE-TIE",100000m,100000m,null,972000m),("PRICE-HIGH",150000m,0m,"PRICE-HIGH",918000m),("PRICE-PCT",200000m,0m,"PRICE-PCT",864000m)})
        {
            var q=await Quote(code);Assert.Equal(discount,q.Discount);Assert.Equal(member,q.MemberDiscount);Assert.Equal(winner,q.Voucher);Assert.Equal(total,q.Total);Assert.Equal(winner==null,q.VoucherIgnored);
#if PARTNER_WINFORMS
            await AssertPartnerQuote(c,q,code);
#endif
        }
        // Vouchers starting/ending later/earlier today use the same timestamp window as POS.
        await Exec("UPDATE public.khuyenmai SET ngaybatdau=(CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh')+INTERVAL '1 hour' WHERE makm='PRICE-LOW'");
        await Assert.ThrowsAsync<CustomerRequestException>(()=>Quote("PRICE-LOW"));
        Assert.DoesNotContain(await features.Promotions(default),p=>(string?)p["MaKM"]=="PRICE-LOW");
        await Exec("UPDATE public.khuyenmai SET ngaybatdau=(CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh')-INTERVAL '1 day' WHERE makm='PRICE-LOW'");

        var request=new CheckoutRequest("Pricing customer","0900000010","Synthetic valid delivery address","COD",null,Guid.NewGuid(),"PRICE-LOW",items);
        var staleQuote=await Assert.ThrowsAsync<CustomerRequestException>(()=>orders.PlaceAsync("KH-PRICE",request with{ExpectedTotal=900000m},default));
        Assert.Equal(409,staleQuote.Status);Assert.Equal(100,await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='PRICE-A'"));
        var placed=await orders.PlaceAsync("KH-PRICE",request,default);var retry=await orders.PlaceAsync("KH-PRICE",request,default);
        Assert.Equal(placed.OrderId,retry.OrderId);Assert.Equal(972000m,placed.Total);
        var detail=await orders.DetailAsync(placed.OrderId,"KH-PRICE",default);Assert.Equal(72000m,detail.VatAmount);Assert.Equal(100000m,detail.Discount);Assert.Null(detail.Voucher);
        Assert.Single(detail.Items);
        Assert.Equal(972000m,await Value<decimal>("SELECT giatrithuctra FROM public.chitiethoadon WHERE sohd='"+placed.OrderId+"'"));
        Assert.Equal(99,await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='PRICE-A'"));
        await Assert.ThrowsAsync<CustomerRequestException>(()=>orders.PlaceAsync("KH-PRICE",request with{Voucher="PRICE-HIGH"},default));
        Assert.Equal(99,await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='PRICE-A'"));

        var winning=request with{RequestId=Guid.NewGuid(),Voucher="PRICE-HIGH"};var winningOrder=await orders.PlaceAsync("KH-PRICE",winning,default);
        // Existing invoices with the original fingerprint remain retryable.
        var legacy=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("KH-PRICE:"+winning.RequestId.ToString("D"))));
        await using(var set=new NpgsqlCommand("UPDATE public.hoadon SET dauvantayyeucau=@fp WHERE sohd=@id",c)){set.Parameters.AddWithValue("fp",legacy);set.Parameters.AddWithValue("id",winningOrder.OrderId);await set.ExecuteNonQueryAsync();}
        Assert.Equal(winningOrder.OrderId,(await orders.PlaceAsync("KH-PRICE",winning,default)).OrderId);
        var mixedRequest=request with{RequestId=Guid.NewGuid(),Voucher=null,Items=new[]{new CartItemRequest{Sku="PRICE-A",Quantity=1},new CartItemRequest{Sku="PRICE-B",Quantity=1}}};
        var mixed=await orders.PlaceAsync("KH-PRICE",mixedRequest,default);Assert.Equal(1620000m,mixed.Total);
        Assert.Equal(120000m,(await orders.DetailAsync(mixed.OrderId,"KH-PRICE",default)).VatAmount);
        Assert.Equal(1620000m,await Value<decimal>("SELECT sum(giatrithuctra) FROM public.chitiethoadon WHERE sohd='"+mixed.OrderId+"'"));
        Assert.Equal(972000m,await Value<decimal>("SELECT giatrithuctra FROM public.chitiethoadon WHERE sohd='"+mixed.OrderId+"' AND masku='PRICE-A'"));

#if PARTNER_WINFORMS
        QLIShopThoiTrang.Api.Data.StoreRepository partner;var previous=Environment.GetEnvironmentVariable("YODYSTORE_CONNECTION_STRING");
        try{Environment.SetEnvironmentVariable("YODYSTORE_CONNECTION_STRING",connection);partner=new(new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:YodyStoreDb"]=connection}).Build()));}
        finally{Environment.SetEnvironmentVariable("YODYSTORE_CONNECTION_STRING",previous);}
        async Task Deliver(string id){foreach(var status in new[]{"Shipper đã lấy hàng","Hàng đang được vận chuyển","Hàng giao thành công"})await partner.UpdateDeliveryStatusAsync(id,new(status,"NV01"));}
        await Deliver(placed.OrderId);await Deliver(winningOrder.OrderId);
        await Assert.ThrowsAsync<CustomerRequestException>(()=>features.CreateReturn("KH-POS",new(){OrderId=placed.OrderId,Sku="PRICE-A",Quantity=1,Kind="HOAN_TIEN",Reason="Wrong owner synthetic",RequestId=Guid.NewGuid()},default));
        await Assert.ThrowsAsync<CustomerRequestException>(()=>features.CreateReturn("KH-PRICE",new(){OrderId=mixed.OrderId,Sku="PRICE-A",Quantity=1,Kind="HOAN_TIEN",Reason="Not received synthetic",RequestId=Guid.NewGuid()},default));
        var returnsBefore=await Value<long>("SELECT count(*) FROM public.phieudoitra");
        var refund=new ReturnInput{OrderId=winningOrder.OrderId,Sku="PRICE-A",Quantity=1,Kind="HOAN_TIEN",Reason="Synthetic refund reason",RequestId=Guid.NewGuid()};
        await features.CreateReturn("KH-PRICE",refund,default);await features.CreateReturn("KH-PRICE",refund,default);
        Assert.Equal(returnsBefore+1,await Value<long>("SELECT count(*) FROM public.phieudoitra"));
        var refundId=await Value<string>("SELECT sophieudt FROM public.phieudoitra WHERE sohd='"+winningOrder.OrderId+"'");
        Assert.Equal("HOAN_TIEN",await Value<string>("SELECT loaigiaodich FROM public.chitietdoitra WHERE sophieudt='"+refundId+"'"));
        Assert.Equal(0m,await Value<decimal>("SELECT thanhtiennhanlai FROM public.chitietdoitra WHERE sophieudt='"+refundId+"'"));
        await partner.ApproveWebReturnAsync(refundId,"NV01");Assert.Equal("HOAN_TAT",await Value<string>("SELECT trangthai FROM public.phieudoitra WHERE sophieudt='"+refundId+"'"));
        Assert.Equal(918000m,await Value<decimal>("SELECT sotienhoan FROM public.phieudoitra WHERE sophieudt='"+refundId+"'"));
        await partner.ApproveWebReturnAsync(refundId,"NV01");
        var exchange=new ReturnInput{OrderId=placed.OrderId,Sku="PRICE-A",Quantity=1,Kind="DOI",NewSku="PRICE-B",Reason="Synthetic exchange reason",RequestId=Guid.NewGuid()};
        await features.CreateReturn("KH-PRICE",exchange,default);
        var exchangeId=await Value<string>("SELECT sophieudt FROM public.phieudoitra WHERE sohd='"+placed.OrderId+"'");
        // Reproduce an old pending request, preserve all fields other than the two codes.
        await Exec("UPDATE public.phieudoitra SET hinhthucxuly='DOI' WHERE sophieudt='"+exchangeId+"';UPDATE public.chitietdoitra SET loaigiaodich='DOI' WHERE sophieudt='"+exchangeId+"';UPDATE public.phieudoitra SET hinhthucxuly='TRA' WHERE sophieudt='"+refundId+"'");
        var lineBefore=await Value<string>("SELECT (to_jsonb(d)-'loaigiaodich')::text FROM public.chitietdoitra d WHERE sophieudt='"+exchangeId+"'");
        await FashionStore.SchemaSetup.WinFormsSyncInstaller.ApplyAsync(connection,Path.Combine(AppContext.BaseDirectory,"Supabase-DongBoWinForms.sql"),Path.Combine(Path.GetTempPath(),settings.Database+"-returns-sync.json"),true);
        Assert.Equal("DOI_SAN_PHAM",await Value<string>("SELECT loaigiaodich FROM public.chitietdoitra WHERE sophieudt='"+exchangeId+"'"));
        Assert.Equal(lineBefore,await Value<string>("SELECT (to_jsonb(d)-'loaigiaodich')::text FROM public.chitietdoitra d WHERE sophieudt='"+exchangeId+"'"));
        Assert.Equal("TRA",await Value<string>("SELECT hinhthucxuly FROM public.phieudoitra WHERE sophieudt='"+refundId+"'"));
        var targetStock=await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='PRICE-B'");
        await partner.ApproveWebReturnAsync(exchangeId,"NV01");await partner.ApproveWebReturnAsync(exchangeId,"NV01");
        Assert.Equal(targetStock-1,await Value<int>("SELECT soluongtonkho FROM public.sanpham WHERE masku='PRICE-B'"));
        Assert.Equal(252000m,await Value<decimal>("SELECT sotienhoan FROM public.phieudoitra WHERE sophieudt='"+exchangeId+"'"));
#endif
    }
#if PARTNER_WINFORMS
    private static async Task AssertPartnerQuote(NpgsqlConnection c,CheckoutQuote quote,string? requestedVoucher)
    {
        var input=new QLIShopThoiTrang.Api.Models.InvoiceCreateDto("PARITY",new("Pricing customer","0900000010"),new(){new("PRICE-A","Pricing A",1,1000000,1000000)},
            quote.Subtotal,quote.Discount,quote.VatAmount,quote.Total,"COD",0,requestedVoucher,true,"Pricing customer","0900000010","Synthetic valid address","NV01");
        var method=typeof(QLIShopThoiTrang.Api.Data.StoreRepository).GetMethod("ValidateInvoicePricingAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
        var selected=await (Task<string?>)method.Invoke(null,new object?[]{c,null,input})!;Assert.Equal(quote.Voucher,selected);
    }
#endif
}
