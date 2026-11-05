using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using FashionStore.Web.Helpers;
using FashionStore.Web.Services;
using FashionStore.Web.Models;
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
        public async Task<ActionResult> Wishlist(){var rows=new JArray();foreach(var id in BrowserWishlistStore.Items()){var detail=await new CatalogRepository().GetDetailAsync(id);if(detail.Product!=null)rows.Add(new JObject{{"MaSP",id},{"TenSanPham",detail.Product.TenSanPham},{"ImageUrl",detail.Product.HinhAnh},{"TrangThai",true}});}return View(rows);}
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
        [AllowAnonymous,HttpGet] public ActionResult Reviews(string id,int page=1,string orderId=null){
            if(String.IsNullOrWhiteSpace(id)||id.Length>20||orderId!=null&&orderId.Length>30)return HttpNotFound();
            if(page<1||page>100000)return new HttpStatusCodeResult(400);
            return Redirect(ProductReviewUrl(id,orderId,page,null,null));
        }
        public async Task<ActionResult> Return(string id,string sku=null) {
            if(String.IsNullOrWhiteSpace(id)||id.Length>30)return HttpNotFound();
            try{
                var options=await api.ReturnOptionsAsync(Token,id);
                var selected=options.Items.FirstOrDefault(x=>x.Sku==sku)??options.Items.FirstOrDefault(x=>x.RemainingQuantity>0)??options.Items.FirstOrDefault();
                if(selected==null)return HttpNotFound();
                var quantity=Math.Min(selected.PurchasedQuantity,selected.RemainingQuantity);
                return View(new ReturnRequestViewModel{Options=options,OrderId=id,RequestId=Guid.NewGuid(),Sku=selected.Sku,NewSku=selected.Sku,Quantity=quantity,NewQuantity=quantity,Recipient=options.Recipient,Phone=options.Phone,ExchangeAddress=options.Address});
            }catch(Exception e){return Error(e);}
        }
        // A return URL is opened with GET after login. Keep legacy bookmarks safe and non-mutating.
        [HttpGet,ActionName("SaveWishlist")] public ActionResult WishlistAfterLogin(){return RedirectToAction("Wishlist");}
        [HttpPost,AllowAnonymous,ValidateAntiForgeryToken]
        public ActionResult SaveWishlist(string id,string returnUrl=null)
        {
            var origin=Url.IsLocalUrl(returnUrl)?returnUrl:Url.Action("Details","Product",new{id});
            var intent=WishlistLoginIntent.Create(id,DateTimeOffset.UtcNow,origin);
            if(intent==null)return new HttpStatusCodeResult(400);
            if(!User.Identity.IsAuthenticated)return LoginForWishlist(intent);
            Session.Remove(WishlistLoginIntent.SessionKey);
            try{
                var saved=!BrowserWishlistStore.Contains(intent.ProductId);
                BrowserWishlistStore.Save(intent.ProductId,!saved);
                var message=saved?"Đã thêm sản phẩm vào danh sách yêu thích.":"Đã bỏ sản phẩm khỏi danh sách yêu thích.";
                if(Request.IsAjaxRequest())return Json(new{productId=intent.ProductId,saved,message});
                TempData["CustomerMessage"]=message;
            }catch(Exception error){
                if(IsUnauthorized(error))return LoginForWishlist(intent);
                if(Request.IsAjaxRequest()){Response.StatusCode=503;Response.TrySkipIisCustomErrors=true;return Json(new{error=Message(error)});}
                TempData["CustomerError"]=Message(error);
            }
            return Redirect(intent.ReturnUrl);
        }
        private ActionResult LoginForWishlist(WishlistLoginIntent intent)
        {
            Session[WishlistLoginIntent.SessionKey]=intent;
            CustomerAuthentication.SignOut();
            if(Request.IsAjaxRequest())return Json(new{loginUrl=Url.Action("Login","Account",new{returnUrl=intent.ReturnUrl})});
            return RedirectToAction("Login","Account",new{returnUrl=intent.ReturnUrl});
        }
        [HttpPost,ValidateAntiForgeryToken] public ActionResult RemoveWishlist(string id){BrowserWishlistStore.Save(id,true);return RedirectToAction("Wishlist");}
        [HttpPost,ValidateAntiForgeryToken] public Task<ActionResult> SaveAddress(string recipient,string phone,string address,long? id=null){return Change("Addresses",()=>api.FeatureAsync(Token,id.HasValue?"addresses/"+id.Value:"addresses",new{Recipient=recipient,Phone=phone,Address=address},id.HasValue?"PUT":"POST"));}
        [HttpPost,ValidateAntiForgeryToken] public Task<ActionResult> RemoveAddress(long id){return Change("Addresses",()=>api.FeatureAsync(Token,"addresses/"+id,null,"DELETE"));}
        [HttpPost,AllowAnonymous,ValidateAntiForgeryToken] public async Task<ActionResult> Review(string productId,string orderId,int stars,string body,string color=null,string returnUrl=null){
            if(String.IsNullOrWhiteSpace(productId)||productId.Length>20)return HttpNotFound();
            var draft=new ProductReviewDraft{ProductId=productId,OrderId=orderId,Stars=stars,Body=body};
            var destination=ProductReviewUrl(productId,orderId,1,color,returnUrl);
            if(!User.Identity.IsAuthenticated){
                draft.Error="Vui lòng đăng nhập để gửi đánh giá.";TempData["ProductReviewDraft"]=draft;
                return RedirectToAction("Login","Account",new{returnUrl=destination});
            }
            try{
                if(!ModelState.IsValid||String.IsNullOrWhiteSpace(orderId)||orderId.Length>30||stars<1||stars>5||String.IsNullOrWhiteSpace(body)||body.Length>2000)
                    throw new CustomerApiException(400,"Vui lòng chọn đơn đã nhận, số sao từ 1 đến 5 và nhập nhận xét tối đa 2.000 ký tự.");
                await api.FeatureAsync(Token,"reviews",new{ProductId=productId,OrderId=orderId,Stars=stars,Body=body});
                TempData["ProductReviewMessage"]="Đã đăng đánh giá của bạn. Nhận xét đã được hiển thị công khai.";
            }
            catch(Exception e){
                draft.Error=Message(e);TempData["ProductReviewDraft"]=draft;
                if(IsUnauthorized(e)){
                    CustomerAuthentication.SignOut();
                    return RedirectToAction("Login","Account",new{returnUrl=destination});
                }
            }
            return Redirect(destination);
        }
        private string ProductReviewUrl(string productId,string orderId,int page,string color,string returnUrl){
            return Url.Action("Details","Product",new{id=productId,reviewOrderId=orderId!=null&&orderId.Length<=30?orderId:null,
                reviewPage=page>1?(int?)page:null,color=color,
                returnUrl=Url.IsLocalUrl(returnUrl)&&returnUrl.StartsWith("/Product",StringComparison.OrdinalIgnoreCase)?returnUrl:null})+"#product-reviews";
        }
        [HttpPost,ValidateAntiForgeryToken] public async Task<ActionResult> SendReturn(ReturnRequestViewModel model){
            if(String.IsNullOrWhiteSpace(model.OrderId)||model.OrderId.Length>30)return HttpNotFound();
            try{
                model.Options=await api.ReturnOptionsAsync(Token,model.OrderId);
                if(model.RequestId==Guid.Empty)ModelState.AddModelError("","Vui lòng tải lại biểu mẫu.");
                if(ModelState.IsValid){await api.FeatureAsync(Token,"returns",new{model.OrderId,model.Sku,model.Quantity,model.Kind,NewSku=model.Kind=="DOI_SAN_PHAM"?model.NewSku:null,NewQuantity=model.Kind=="DOI_SAN_PHAM"?model.NewQuantity:null,model.Reason,model.RequestId,model.Recipient,model.Phone,model.ExchangeAddress,model.AccountHolder,model.AccountNumber,model.BankName});TempData["CustomerMessage"]="Đã gửi yêu cầu. Cửa hàng sẽ kiểm tra biến thể, số lượng và chênh lệch trước khi duyệt.";return RedirectToAction("Returns");}
            }catch(Exception e){if(IsUnauthorized(e))return Error(e);ModelState.AddModelError("",Message(e));if(model.Options==null)return Error(e);}
            return View("Return",model);
        }
        [HttpPost,ValidateAntiForgeryToken] public async Task<ActionResult> SaveReturnRefund(string returnId,string accountHolder,string accountNumber,string bankName){
            if(String.IsNullOrWhiteSpace(returnId)||returnId.Length>30)return HttpNotFound();
            try{await api.FeatureAsync(Token,"returns/"+Uri.EscapeDataString(returnId)+"/refund-account",new{AccountHolder=accountHolder,AccountNumber=accountNumber,BankName=bankName});TempData["CustomerMessage"]="Đã lưu tài khoản cho đúng phiếu đổi/trả. Cửa hàng sẽ đối soát trước khi hoàn tiền.";}
            catch(Exception e){if(IsUnauthorized(e))return Error(e);TempData["CustomerError"]=Message(e);}
            return RedirectToAction("Returns");
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
