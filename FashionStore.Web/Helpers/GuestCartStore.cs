using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using FashionStore.Web.Models;
using FashionStore.Web.Services;
namespace FashionStore.Web.Helpers
{
    public static class GuestCartStore
    {
        public static List<GuestCartEntry> Items(HttpSessionStateBase session){return session["GuestCart"] as List<GuestCartEntry>??new List<GuestCartEntry>();}
        public static void Save(HttpSessionStateBase session,List<GuestCartEntry> items){session["GuestCart"]=items;session.Remove("GuestMergeId");}
        public static Guid MergeId(HttpSessionStateBase session){if(!(session["GuestMergeId"] is Guid))session["GuestMergeId"]=Guid.NewGuid();return (Guid)session["GuestMergeId"];}
        public static void Clear(HttpSessionStateBase session){session.Remove("GuestCart");session.Remove("GuestMergeId");}
        public static async Task<CustomerCart> ReadAsync(HttpSessionStateBase session)
        {
            var items=Items(session);var api=new CatalogRepository();
            var products=await Task.WhenAll(items.Select(i=>i.ProductId).Distinct().Select(async id=>new{Id=id,Detail=await api.GetDetailAsync(id)}));
            var cart=new CustomerCart{IsGuest=true,SupportsVouchers=false};
            foreach(var entry in items){
                var detail=products.First(p=>p.Id==entry.ProductId).Detail;var variant=detail.Variants.FirstOrDefault(v=>v.MaSKU==entry.Sku);
                var picture=detail.Images.FirstOrDefault(p=>p.MaSKU==entry.Sku)??detail.Images.FirstOrDefault(p=>variant!=null && p.MauSac==variant.MauSac)??detail.Images.FirstOrDefault();
                var available=detail.Product!=null && variant!=null && variant.SoLuongTonKho>=entry.Quantity;
                cart.Items.Add(new CustomerCartLine{Sku=entry.Sku,ProductId=entry.ProductId,ProductName=detail.Product==null?"Sản phẩm không còn bán":detail.Product.TenSanPham,
                    ImageUrl=picture==null?(detail.Product==null?null:detail.Product.HinhAnh):picture.DuongDan,Size=variant==null?"":variant.KichCo,Color=variant==null?"":variant.MauSac,
                    UnitPrice=variant==null?0:variant.DonGia,LineTotal=variant==null?0:variant.DonGia*entry.Quantity,Stock=variant==null?0:variant.SoLuongTonKho,Quantity=entry.Quantity,
                    Available=available,Issue=available?null:"Sản phẩm hoặc số lượng không còn khả dụng."});
            }
            cart.TotalQuantity=cart.Items.Sum(i=>(long)i.Quantity);cart.Subtotal=cart.Items.Sum(i=>i.LineTotal);cart.CanCheckout=cart.Items.Count>0&&cart.Items.All(i=>i.Available);
            return cart;
        }
    }
}
