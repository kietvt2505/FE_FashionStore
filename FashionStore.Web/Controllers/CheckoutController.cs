using System;
using System.Threading.Tasks;
using System.Web.Mvc;
using FashionStore.Web.Helpers;
using FashionStore.Web.Models;
using FashionStore.Web.Services;

namespace FashionStore.Web.Controllers
{
    [CustomerCartAuthorize, OutputCache(NoStore=true,Duration=0,VaryByParam="*")]
    public sealed class CheckoutController : Controller
    {
        private readonly CustomerApiClient api = new CustomerApiClient();
        [HttpGet]
        public async Task<ActionResult> Index(long? addressId=null,string voucher=null)
        {
            var token = CustomerAuthentication.Token(Request);
            if (String.IsNullOrEmpty(token)) return LoginAgain();
            try
            {
                var cart = await api.CartAsync(token);
                if (cart.Items.Count == 0 || !cart.CanCheckout || !cart.CheckoutEnabled) return RedirectToAction("Index", "Cart");
                var customer = await api.ProfileAsync(token);
                if(addressId.HasValue) {
                    var addresses=await api.FeatureListAsync(token,"addresses");
                    foreach(var address in addresses) if((long)address["Id"]==addressId.Value) {
                        customer.FullName=(string)address["Recipient"];customer.Phone=(string)address["Phone"];customer.Address=(string)address["Address"];break;
                    }
                }
                var model=new CheckoutViewModel {
                    Recipient = customer.FullName, Phone = customer.Phone, Address = customer.Address,
                    PaymentMethod = "COD", RequestId = Guid.NewGuid(), Cart = cart,Voucher=voucher
                };
                await ApplyRequestedQuote(model,token);
                model.OnlineMethods=await api.PaymentOptionsAsync(token);
                return View(model);
            }
            catch (CustomerApiException e) { if (e.StatusCode == 401) return LoginAgain(); return Unavailable(); }
            catch (Exception) { return Unavailable(); }
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Index([Bind(Include="Recipient,Phone,Address,PaymentMethod,Note,RequestId,Voucher")]CheckoutViewModel model,string checkoutAction=null)
        {
            var token = CustomerAuthentication.Token(Request);
            if (String.IsNullOrEmpty(token)) return LoginAgain();
            var preview=checkoutAction=="applyVoucher"||checkoutAction=="removeVoucher";
            if(preview) {ModelState.Clear();if(checkoutAction=="removeVoucher")model.Voucher=null;if(model.RequestId==Guid.Empty)model.RequestId=Guid.NewGuid();}
            else if (model.RequestId == Guid.Empty) ModelState.AddModelError("", "Mã yêu cầu không hợp lệ. Vui lòng tải lại trang thanh toán.");
            if (!preview && ModelState.IsValid)
            {
                try
                {
                    var result = await api.PlaceOrderAsync(token, model);
                    if(result.Status!="CAN_HO_TRO" && CustomerViewHelper.IsOnlinePayment(result.PaymentMethod))
                    {
                        try{return Redirect((await api.StartPaymentAsync(token,result.OrderId,Request.UserHostAddress)).Url);}
                        catch(Exception){TempData["CustomerError"]="Đơn hàng đã được lưu nhưng chưa mở được cổng thanh toán. Bạn có thể tiếp tục từ trang chi tiết đơn.";return RedirectToAction("Details","Orders",new{id=result.OrderId});}
                    }
                    if (result.Status != "CAN_HO_TRO") TempData["OrderSuccess"]=result.OrderId;
                    else TempData["CustomerError"]="Đơn hàng cần cửa hàng kiểm tra. Yêu cầu này không tạo thêm đơn mới.";
                    return RedirectToAction("Details", "Orders", new { id = result.OrderId });
                }
                catch (CustomerApiException e)
                {
                    if (e.StatusCode == 401) return LoginAgain();
                    ModelState.AddModelError("", e.Message);
                }
                catch (Exception) { ModelState.AddModelError("", "Không rõ đơn hàng đã được tạo chưa. Vui lòng kiểm tra lịch sử trước khi thử lại."); }
            }
            try { model.Cart = await api.CartAsync(token);model.OnlineMethods=await api.PaymentOptionsAsync(token); if(model.Cart.Items.Count>0)await ApplyRequestedQuote(model,token); }
            catch (CustomerApiException e) { if (e.StatusCode == 401) return LoginAgain(); return Unavailable(); }
            catch (Exception) { return Unavailable(); }
            if (model.Cart.Items.Count == 0) return RedirectToAction("Index", "Orders");
            return View(model);
        }
        private async Task ApplyRequestedQuote(CheckoutViewModel model,string token)
        {
            try {
                if(!String.IsNullOrWhiteSpace(model.Voucher)&&model.Voucher.Trim().Length>20) {
                    ModelState.AddModelError("Voucher","Mã giảm giá tối đa 20 ký tự.");ApplyQuote(model,await api.QuoteAsync(token));return;
                }
                ApplyQuote(model,await api.QuoteAsync(token,model.Voucher));
            }
            catch(CustomerApiException e) when(e.StatusCode!=401&&!String.IsNullOrWhiteSpace(model.Voucher)) {
                ModelState.AddModelError("Voucher",e.Message);
                ApplyQuote(model,await api.QuoteAsync(token));
            }
        }
        private static void ApplyQuote(CheckoutViewModel model,Newtonsoft.Json.Linq.JObject quote)
        {
            model.QuotedDiscount=(decimal)quote["discount"];model.QuotedTotal=(decimal)quote["total"];
            model.MembershipEnabled=(bool?)quote["membershipEnabled"]??false;
            model.MemberRank=(string)quote["memberRank"];model.MemberRate=(int?)quote["memberRate"]??0;
            model.MemberPoints=(int?)quote["memberPoints"]??0;
            model.QuotedMemberDiscount=(decimal?)quote["memberDiscount"]??(model.MembershipEnabled?(model.QuotedDiscount??0):0);
            model.QuotedVoucherDiscount=(decimal?)quote["voucherDiscount"]??0;model.AppliedVoucher=(string)quote["voucher"];
        }
        private ActionResult LoginAgain() { CustomerAuthentication.SignOut(); return RedirectToAction("Login","Account",new { returnUrl=Url.Action("Index") }); }
        private ActionResult Unavailable() { Response.StatusCode = 503; Response.TrySkipIisCustomErrors = true; ViewBag.CustomerError="Không thể tải dữ liệu thanh toán. Vui lòng thử lại sau."; return View("~/Views/Account/CustomerError.cshtml"); }
    }
}
