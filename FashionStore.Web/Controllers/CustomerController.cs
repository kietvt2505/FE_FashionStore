using System;
using System.Threading.Tasks;
using System.Web.Mvc;
using FashionStore.Web.Helpers;
using FashionStore.Web.Services;
using Newtonsoft.Json.Linq;
namespace FashionStore.Web.Controllers
{
    [Authorize,OutputCache(NoStore=true,Duration=0,VaryByParam="*")]
    public sealed class CustomerController:Controller
    {
        private readonly CustomerApiClient api=new CustomerApiClient();
        private string Token {get{return CustomerAuthentication.Token(Request);}}
        public async Task<ActionResult> Index() {
            try{return View(await api.ProfileAsync(Token));}catch(Exception e){return Error(e);}
        }
        [AllowAnonymous] public async Task<ActionResult> Promotions(){
            try{
                var promotions=await api.FeatureListAsync(Token,"promotions?page=1");
                if(User.Identity.IsAuthenticated)ViewBag.MembershipProfile=await api.ProfileAsync(Token);
                return View(promotions);
            }catch(Exception e){return Error(e);}
        }
        public async Task<ActionResult> Security(){
            try{return View(await api.ProfileAsync(Token));}catch(Exception e){return Error(e);}
        }
        public Task<ActionResult> Points(int page=1){return List("Points","points",page);}
        public Task<ActionResult> Wishlist(){return List("Wishlist","wishlist",1);}
        public async Task<ActionResult> Addresses(long? editId=null){
            try{
                var addresses=await api.FeatureListAsync(Token,"addresses");
                if(editId.HasValue){
                    foreach(var address in addresses) if((long)address["Id"]==editId.Value){ViewBag.EditAddress=address;break;}
                    if(ViewBag.EditAddress==null)return HttpNotFound();
                }
                return View(addresses);
            }catch(Exception e){return Error(e);}
        }
        public Task<ActionResult> Returns(int page=1){return List("Returns","returns",page);}
        [AllowAnonymous] public async Task<ActionResult> Reviews(string id,int page=1,string orderId=null){
            if(String.IsNullOrWhiteSpace(id)||id.Length>20||orderId!=null&&orderId.Length>30)return HttpNotFound();
            if(page<1||page>100000)return new HttpStatusCodeResult(400);
            ViewBag.ProductId=id;ViewBag.ReviewOrderId=orderId;ViewBag.Page=page;
            ViewBag.ReviewOrders=new JArray();
            try{
                var reviews=await api.FeatureListAsync(Token,"reviews/"+Uri.EscapeDataString(id)+"?page="+page);
                if(User.Identity.IsAuthenticated){
                    try{ViewBag.ReviewOrders=await api.FeatureListAsync(Token,"reviews/"+Uri.EscapeDataString(id)+"/eligible-orders");}
                    catch(Exception e){if(IsUnauthorized(e))return Error(e);ViewBag.ReviewEligibilityError="Chưa thể kiểm tra đơn đủ điều kiện đánh giá. Vui lòng thử lại sau.";}
                }
                return View("Reviews",reviews);
            }catch(Exception e){return Error(e);}
        }
        public async Task<ActionResult> Return(string id) {
            if(String.IsNullOrWhiteSpace(id)||id.Length>30)return HttpNotFound();
            try{ViewBag.RequestId=Guid.NewGuid();return View(await api.OrderAsync(Token,id));}catch(Exception e){return Error(e);}
        }
        // A return URL is opened with GET after login. Keep legacy bookmarks safe and non-mutating.
        [HttpGet,ActionName("SaveWishlist")] public ActionResult WishlistAfterLogin(){return RedirectToAction("Wishlist");}
        [HttpPost,AllowAnonymous,ValidateAntiForgeryToken]
        public async Task<ActionResult> SaveWishlist(string id)
        {
            var intent=WishlistLoginIntent.Create(id,DateTimeOffset.UtcNow);
            if(intent==null)return new HttpStatusCodeResult(400);
            if(!User.Identity.IsAuthenticated)return LoginForWishlist(intent);
            Session.Remove(WishlistLoginIntent.SessionKey);
            try{
                await api.FeatureAsync(Token,"wishlist/"+Uri.EscapeDataString(intent.ProductId),null,"PUT");
                TempData["CustomerMessage"]="Đã thêm sản phẩm vào danh sách yêu thích.";
            }catch(Exception error){
                if(IsUnauthorized(error))return LoginForWishlist(intent);
                TempData["CustomerError"]=Message(error);
            }
            return RedirectToAction("Wishlist");
        }
        private ActionResult LoginForWishlist(WishlistLoginIntent intent)
        {
            Session[WishlistLoginIntent.SessionKey]=intent;
            CustomerAuthentication.SignOut();
            return RedirectToAction("Login","Account",new{returnUrl=Url.Action("Wishlist","Customer")});
        }
        [HttpPost,ValidateAntiForgeryToken] public Task<ActionResult> RemoveWishlist(string id){return Change("Wishlist",()=>api.FeatureAsync(Token,"wishlist/"+Uri.EscapeDataString(id??""),null,"DELETE"));}
        [HttpPost,ValidateAntiForgeryToken] public Task<ActionResult> SaveAddress(string recipient,string phone,string address,long? id=null){return Change("Addresses",()=>api.FeatureAsync(Token,id.HasValue?"addresses/"+id.Value:"addresses",new{Recipient=recipient,Phone=phone,Address=address},id.HasValue?"PUT":"POST"));}
        [HttpPost,ValidateAntiForgeryToken] public Task<ActionResult> RemoveAddress(long id){return Change("Addresses",()=>api.FeatureAsync(Token,"addresses/"+id,null,"DELETE"));}
        [HttpPost,ValidateAntiForgeryToken] public async Task<ActionResult> Review(string productId,string orderId,int stars,string body){
            try{await api.FeatureAsync(Token,"reviews",new{ProductId=productId,OrderId=orderId,Stars=stars,Body=body});TempData["CustomerMessage"]="Đã gửi đánh giá, đang chờ cửa hàng duyệt.";}
            catch(Exception e){if(IsUnauthorized(e))return Error(e);TempData["CustomerError"]=Message(e);}
            return RedirectToAction("Reviews",new{id=productId,orderId=orderId});
        }
        [HttpPost,ValidateAntiForgeryToken] public async Task<ActionResult> SendReturn(string orderId,string sku,int quantity,string kind,string newSku,string reason,Guid requestId){
            try{await api.FeatureAsync(Token,"returns",new{OrderId=orderId,Sku=sku,Quantity=quantity,Kind=kind,NewSku=newSku,Reason=reason,RequestId=requestId});TempData["CustomerMessage"]="Đã gửi yêu cầu. Cửa hàng sẽ kiểm tra trước khi duyệt.";return RedirectToAction("Returns");}
            catch(Exception e){if(IsUnauthorized(e))return Error(e);TempData["CustomerError"]=Message(e);return RedirectToAction("Return",new{id=orderId});}
        }
        private async Task<ActionResult> List(string view,string path,int page){
            if(page<1||page>100000)return new HttpStatusCodeResult(400);
            ViewBag.Page=page;
            try{return View(view,await api.FeatureListAsync(Token,path+"?page="+page));}catch(Exception e){return Error(e);}
        }
        private async Task<ActionResult> Change(string action,Func<Task<object>> call){
            try{await call();TempData["CustomerMessage"]="Đã cập nhật.";}catch(Exception e){if(IsUnauthorized(e))return Error(e);TempData["CustomerError"]=Message(e);}return RedirectToAction(action);
        }
        private static bool IsUnauthorized(Exception e){var apiError=e as CustomerApiException;return apiError!=null&&apiError.StatusCode==401;}
        private static string Message(Exception e){return e is CustomerApiException?e.Message:"Chưa thể kết nối dịch vụ. Vui lòng thử lại.";}
        private ActionResult Error(Exception e){if(IsUnauthorized(e)){CustomerAuthentication.SignOut();return RedirectToAction("Login","Account");}Response.StatusCode=503;Response.TrySkipIisCustomErrors=true;ViewBag.CustomerError=Message(e);return View("~/Views/Account/CustomerError.cshtml");}
    }
}
