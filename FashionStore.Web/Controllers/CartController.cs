using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using FashionStore.Web.Helpers;
using FashionStore.Web.Models;
using FashionStore.Web.Services;
namespace FashionStore.Web.Controllers
{
    [OutputCache(NoStore=true,Duration=0,VaryByParam="*")]
    public class CartController:Controller
    {
        private readonly CustomerApiClient api=new CustomerApiClient();
        private bool Guest {get{return !User.Identity.IsAuthenticated;}}
        [HttpGet] public async Task<ActionResult> Count() {
            if(Guest)return Json(new{count=GuestCartStore.Items(Session).Sum(i=>(long)i.Quantity)},JsonRequestBehavior.AllowGet);
            try{return Json(new{count=(await api.CartAsync(CustomerAuthentication.Token(Request))).TotalQuantity},JsonRequestBehavior.AllowGet);}
            catch(Exception){return new HttpStatusCodeResult(503);}
        }
        private void ReturnLink(string url=null) {
            if(Url.IsLocalUrl(url)&&url.StartsWith("/Product",StringComparison.OrdinalIgnoreCase))Session["ShoppingReturnUrl"]=url;
            var saved=Session["ShoppingReturnUrl"] as string;
            ViewBag.ContinueUrl=Url.IsLocalUrl(saved)?saved:Url.Action("Index","Product");
        }
        [HttpGet] public async Task<ActionResult> Index() {
            ReturnLink();
            try{return View(Guest?await GuestCartStore.ReadAsync(Session):await api.CartAsync(CustomerAuthentication.Token(Request)));}
            catch(CustomerApiException e){if(e.StatusCode==401)return LoginAgain();return Error(e.Message);}
            catch(Exception){return Error("Chưa thể tải giỏ hàng. Vui lòng thử lại.");}
        }
        [HttpPost,ValidateAntiForgeryToken] public async Task<ActionResult> Add([Bind(Prefix="selected-sku")]string sku,int quantity=1,string productId=null,string returnUrl=null) {
            ReturnLink(returnUrl);
            if(!ModelState.IsValid||quantity<1||String.IsNullOrWhiteSpace(sku)||sku.Length>40||String.IsNullOrWhiteSpace(productId)||productId.Length>20){TempData["CartError"]="Vui lòng chọn đúng phân loại và số lượng.";return RedirectToAction("Details","Product",new{id=productId,returnUrl});}
            try {
                if(Guest) {
                    var detail=await new CatalogRepository().GetDetailAsync(productId??"");
                    var variant=detail.Variants.FirstOrDefault(v=>v.MaSKU==sku);
                    if(detail.Product==null||variant==null)throw new InvalidOperationException("Phân loại không còn bán.");
                    var items=GuestCartStore.Items(Session);var item=items.FirstOrDefault(i=>i.Sku==sku);
                    var next=(long)(item==null?0:item.Quantity)+quantity;
                    if(next>variant.SoLuongTonKho)throw new InvalidOperationException("Số lượng vượt quá tồn kho hiện tại.");
                    if(item==null){if(items.Count>=50)throw new InvalidOperationException("Giỏ hàng chỉ lưu tối đa 50 phân loại.");items.Add(new GuestCartEntry{ProductId=productId,Sku=sku,Quantity=(int)next});}
                    else item.Quantity=(int)next;
                    GuestCartStore.Save(Session,items);
                } else await api.AddAsync(CustomerAuthentication.Token(Request),sku,quantity);
                TempData["CustomerMessage"]="Đã thêm sản phẩm vào giỏ hàng.";return RedirectToAction("Index");
            } catch(CustomerApiException e){if(e.StatusCode==401)return LoginAgain();TempData["CartError"]=e.Message;}
            catch(InvalidOperationException e){TempData["CartError"]=e.Message;}
            catch(Exception){TempData["CartError"]="Chưa thể thêm sản phẩm. Vui lòng thử lại.";}
            return RedirectToAction("Details","Product",new{id=productId,returnUrl});
        }
        [HttpPost,ValidateAntiForgeryToken] public async Task<ActionResult> Update(string sku,int quantity) {
            try {
                if(!ModelState.IsValid||quantity<1)throw new InvalidOperationException("Số lượng phải là số nguyên lớn hơn 0.");
                if(Guest) {
                    var cart=await GuestCartStore.ReadAsync(Session);var row=cart.Items.FirstOrDefault(i=>i.Sku==sku);
                    if(row==null||quantity>row.Stock)throw new InvalidOperationException("Số lượng vượt quá tồn kho hiện tại.");
                    var items=GuestCartStore.Items(Session);items.First(i=>i.Sku==sku).Quantity=quantity;GuestCartStore.Save(Session,items);
                }else await api.SetAsync(CustomerAuthentication.Token(Request),sku,quantity);
            } catch(CustomerApiException e){if(e.StatusCode==401)return LoginAgain();QuantityError(sku,e.Message);}
            catch(InvalidOperationException e){QuantityError(sku,e.Message);}
            catch(Exception){QuantityError(sku,"Chưa thể cập nhật giỏ hàng. Vui lòng thử lại.");}
            return RedirectToAction("Index");
        }
        private void QuantityError(string sku,string message){TempData["CartError"]=message;TempData["InvalidQuantitySku"]=sku;TempData["InvalidQuantityValue"]=Request.Form["quantity"];}
        [HttpPost,ValidateAntiForgeryToken] public async Task<ActionResult> Remove(string sku) {
            if(Guest){GuestCartStore.Save(Session,GuestCartStore.Items(Session).Where(i=>i.Sku!=sku).ToList());return RedirectToAction("Index");}
            return await Change(()=>api.DeleteAsync(CustomerAuthentication.Token(Request),sku));
        }
        [HttpPost,ValidateAntiForgeryToken] public async Task<ActionResult> Clear() {
            if(Guest){GuestCartStore.Clear(Session);return RedirectToAction("Index");}
            return await Change(()=>api.ClearAsync(CustomerAuthentication.Token(Request)));
        }
        [HttpPost,CustomerCartAuthorize,ValidateAntiForgeryToken] public async Task<ActionResult> Merge(bool accept) {
            if(!accept){GuestCartStore.Clear(Session);TempData.Remove("GuestMergeError");return RedirectToAction("Index");}
            try{
                var items=GuestCartStore.Items(Session);
                if(items.Count>0)await api.MergeGuestAsync(CustomerAuthentication.Token(Request),GuestCartStore.MergeId(Session),items);
                GuestCartStore.Clear(Session);TempData.Remove("GuestMergeError");TempData["CustomerMessage"]="Đã gộp giỏ hàng vào tài khoản.";
            }catch(CustomerApiException e){if(e.StatusCode==401)return LoginAgain();MergeError(e.Message);}
            catch(Exception){MergeError("Chưa thể xác nhận gộp giỏ. Sản phẩm vẫn được giữ lại. Bạn có thể thử lại sau.");}
            return RedirectToAction("Index");
        }
        private void MergeError(string message){TempData["GuestMergeError"]=message;TempData["PromptGuestMerge"]=true;}
        private async Task<ActionResult> Change(Func<Task<CustomerCart>> action) {
            try{await action();}catch(CustomerApiException e){if(e.StatusCode==401)return LoginAgain();TempData["CartError"]=e.Message;}
            catch(Exception){TempData["CartError"]="Chưa thể cập nhật giỏ hàng. Vui lòng thử lại.";}
            return RedirectToAction("Index");
        }
        private ActionResult Error(string message){Response.StatusCode=503;Response.TrySkipIisCustomErrors=true;return View("Index",new CustomerCart{Error=message,IsGuest=Guest});}
        private ActionResult LoginAgain(){CustomerAuthentication.SignOut();return RedirectToAction("Login","Account",new{returnUrl=Url.Action("Index")});}
    }
}
