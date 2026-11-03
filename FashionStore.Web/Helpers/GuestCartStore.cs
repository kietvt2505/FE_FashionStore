using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using FashionStore.Web.Models;
using FashionStore.Web.Services;
namespace FashionStore.Web.Helpers
{
    public static class BrowserWishlistStore
    {
        public static string Read(string name){try{var value=HttpContext.Current.Items[name] as string??HttpContext.Current.Request.Cookies[name]?.Value;return String.IsNullOrEmpty(value)?null:System.Text.Encoding.UTF8.GetString(System.Web.Security.MachineKey.Unprotect(Convert.FromBase64String(value),name));}catch{return null;}}
        public static void Write(string name,string value){var data=Convert.ToBase64String(System.Web.Security.MachineKey.Protect(System.Text.Encoding.UTF8.GetBytes(value),name));if(data.Length>3700)throw new InvalidOperationException("Danh sách trên trình duyệt đã đầy. Vui lòng bớt sản phẩm.");HttpContext.Current.Items[name]=data;HttpContext.Current.Response.Cookies.Set(new HttpCookie(name,data){HttpOnly=true,Secure=HttpContext.Current.Request.IsSecureConnection,SameSite=SameSiteMode.Lax,Path="/",Expires=DateTime.UtcNow.AddDays(30)});}
        public static List<string> Items(){try{return Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(Read("fs-wishlist")??"[]")??new List<string>();}catch{return new List<string>();}}
        public static bool Contains(string id){return Items().Contains(id);}
        // Explicit selection makes login retries idempotent and preserves the original order.
        public static void Save(string id,bool remove=false){var items=Items();WishlistSelection.Set(items,id,!remove);Write("fs-wishlist",Newtonsoft.Json.JsonConvert.SerializeObject(items));}
    }
    public static class GuestCartStore
    {
        public static List<GuestCartEntry> Items(HttpSessionStateBase session){var raw=BrowserWishlistStore.Read("fs-cart"); try{return String.IsNullOrEmpty(raw)?new List<GuestCartEntry>():Newtonsoft.Json.JsonConvert.DeserializeObject<List<GuestCartEntry>>(raw)??new List<GuestCartEntry>();}catch{return new List<GuestCartEntry>();}}
        public static void Save(HttpSessionStateBase session,List<GuestCartEntry> items){BrowserWishlistStore.Write("fs-cart",Newtonsoft.Json.JsonConvert.SerializeObject(items));session.Remove("GuestMergeId");}
        public static Guid MergeId(HttpSessionStateBase session){if(!(session["GuestMergeId"] is Guid))session["GuestMergeId"]=Guid.NewGuid();return (Guid)session["GuestMergeId"];}
        public static void Clear(HttpSessionStateBase session){Save(session,new List<GuestCartEntry>());}
        public static async Task<CustomerCart> ReadAsync(HttpSessionStateBase session)
        {
            var items=Items(session);var api=new CatalogRepository();
            var products=await Task.WhenAll(items.Select(i=>i.ProductId).Distinct().Select(async id=>new{Id=id,Detail=await api.GetDetailAsync(id)}));
            var cart=new CustomerCart{IsGuest=!HttpContext.Current.User.Identity.IsAuthenticated,SupportsVouchers=true};
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
