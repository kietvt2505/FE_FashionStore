using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using FashionStore.Web.Helpers;
using FashionStore.Web.Models;
using FashionStore.Web.Services;

namespace FashionStore.Web.Controllers
{
    [CustomerCartAuthorize, OutputCache(NoStore=true,Duration=0,VaryByParam="*")]
    public sealed class OrdersController : Controller
    {
        private readonly CustomerApiClient api = new CustomerApiClient();
        [HttpGet] public async Task<ActionResult> Index(string q=null,string status=null,int page=1)
        {
            if(page<1||page>100000)return new HttpStatusCodeResult(400);
            ViewBag.Query=q;ViewBag.Status=status;ViewBag.Page=page;
            try { return View(await api.OrdersAsync(CustomerAuthentication.Token(Request),q,status,page)); }
            catch (CustomerApiException e) { if (e.StatusCode == 401) return LoginAgain(); return Unavailable(); }
            catch (Exception) { return Unavailable(); }
        }
        [HttpGet] public async Task<ActionResult> Details(string id)
        {
            if (String.IsNullOrWhiteSpace(id)) return HttpNotFound();
            try {
                var model=await api.OrderAsync(CustomerAuthentication.Token(Request),id);
                if(model.SupportsHistory) try{ViewBag.History=await api.FeatureListAsync(CustomerAuthentication.Token(Request),"orders/"+Uri.EscapeDataString(id)+"/history");}catch(Exception){ViewBag.HistoryError="Chưa thể tải lịch sử xử lý.";}
                return View(model);
            }
            catch (CustomerApiException e)
            {
                if (e.StatusCode == 401) return LoginAgain();
                if (e.StatusCode == 404) return HttpNotFound();
                return Unavailable();
            }
            catch (Exception) { return Unavailable(); }
        }
        [HttpPost,ValidateAntiForgeryToken] public Task<ActionResult> Cancel(CancelOrderViewModel model) { return SubmitCancellation(model,false); }
        [HttpPost,ValidateAntiForgeryToken] public Task<ActionResult> RefundAccount(CancelOrderViewModel model) { return SubmitCancellation(model,true); }
        private async Task<ActionResult> SubmitCancellation(CancelOrderViewModel model,bool accountOnly) {
            if(String.IsNullOrWhiteSpace(model.Id)||model.Id.Length>30)return HttpNotFound();
            OrderDetailsModel order;
            try { order=await api.OrderAsync(CustomerAuthentication.Token(Request),model.Id); }
            catch(CustomerApiException e) {if(e.StatusCode==401)return LoginAgain();if(e.StatusCode==404)return HttpNotFound();return Unavailable();}
            catch(Exception){return Unavailable();}
            model.RequiresReason=!accountOnly;
            model.RequiresRefund=accountOnly||order.PaymentStatus=="DA_THANH_TOAN";
            model.RefundAccountOnly=accountOnly;
            model.CanUpdateDelivery=order.CanUpdateDelivery;model.CanUpdatePayment=order.CanUpdatePayment;
            model.PaymentUpdateNotice=order.PaymentUpdateNotice;
            if(accountOnly?order.Refund==null||order.Refund.Status!="CHO_THONG_TIN":!order.CanCancel)
                ModelState.AddModelError("","Trạng thái đơn đã thay đổi. Vui lòng tải lại trang để xem kết quả mới nhất.");
            if(!accountOnly){
                switch(model.ReasonCode){
                    case "NO_NEED": model.Reason="Tôi không còn nhu cầu mua hàng";break;
                    case "ADDRESS": model.Reason="Tôi muốn thay đổi địa chỉ nhận hàng";break;
                    case "PAYMENT": model.Reason="Tôi muốn thay đổi phương thức thanh toán";break;
                    case "OTHER":
                        if(String.IsNullOrWhiteSpace(model.Reason)||model.Reason.Trim().Length<5)
                            ModelState.AddModelError("Reason","Vui lòng nhập lý do khác từ 5–500 ký tự.");
                        break;
                    default: ModelState.AddModelError("ReasonCode","Vui lòng chọn lý do hủy đơn.");break;
                }
            }
            if(model.RequiresRefund) {
                if(String.IsNullOrWhiteSpace(model.AccountHolder)||model.AccountHolder.Trim().Length<2)ModelState.AddModelError("AccountHolder","Vui lòng nhập tên chủ tài khoản.");
                if(String.IsNullOrWhiteSpace(model.AccountNumber))ModelState.AddModelError("AccountNumber","Vui lòng nhập số tài khoản.");
                if(String.IsNullOrWhiteSpace(model.BankName)||model.BankName.Trim().Length<2)ModelState.AddModelError("BankName","Vui lòng nhập tên ngân hàng.");
            }
            if(ModelState.IsValid) try {
                if(accountOnly)await api.RefundAccountAsync(CustomerAuthentication.Token(Request),model.Id,model);
                else await api.CancelAsync(CustomerAuthentication.Token(Request),model.Id,model);
                TempData["CustomerMessage"]=accountOnly?"Đã gửi thông tin nhận hoàn tiền. Cửa hàng sẽ đối soát và xử lý.":
                    model.RequiresRefund?"Đã hủy đơn và gửi yêu cầu hoàn tiền. Cửa hàng sẽ kiểm tra trước khi chuyển tiền.":"Đã hủy đơn hàng.";
                return RedirectToAction("Details",new{id=model.Id});
            }
            catch(CustomerApiException e){if(e.StatusCode==401)return LoginAgain();ModelState.AddModelError("",e.Message);}
            catch(Exception){ModelState.AddModelError("","Chưa xác nhận được kết quả. Vui lòng kiểm tra lại đơn hàng trước khi thử lại.");}
            model.Open=true;ViewBag.CancellationForm=model;
            if(order.SupportsHistory)try{ViewBag.History=await api.FeatureListAsync(CustomerAuthentication.Token(Request),"orders/"+Uri.EscapeDataString(model.Id)+"/history");}catch(Exception){ViewBag.HistoryError="Chưa thể tải lịch sử xử lý.";}
            return View("Details",order);
        }
        [HttpGet] public async Task<ActionResult> Update(string id,string section="delivery")
        {
            if(String.IsNullOrWhiteSpace(id)||id.Length>30)return HttpNotFound();
            if(section!="delivery"&&section!="payment")return new HttpStatusCodeResult(400);
            try{
                var order=await api.OrderAsync(CustomerAuthentication.Token(Request),id);
                if(!CanUpdate(order,section)){TempData["CustomerError"]=UpdateUnavailable(order,section);return RedirectToAction("Details",new{id});}
                var model=new UpdateOrderViewModel{Id=id,Section=section,Version=order.EditVersion,Recipient=order.Recipient,
                    Phone=order.Phone,Address=order.Address,PaymentMethod=order.PaymentMethod,Order=order};
                if(section=="payment")model.OnlineMethods=await api.PaymentOptionsAsync(CustomerAuthentication.Token(Request));
                return View(model);
            }catch(CustomerApiException e){if(e.StatusCode==401)return LoginAgain();if(e.StatusCode==404)return HttpNotFound();return Unavailable();}
            catch(Exception){return Unavailable();}
        }
        [HttpPost,ValidateAntiForgeryToken] public async Task<ActionResult> Update([Bind(Include="Id,Section,Version,Recipient,Phone,Address,PaymentMethod")]UpdateOrderViewModel model)
        {
            if(String.IsNullOrWhiteSpace(model.Id)||model.Id.Length>30)return HttpNotFound();
            if(model.Section!="delivery"&&model.Section!="payment")return new HttpStatusCodeResult(400);
            try{
                var token=CustomerAuthentication.Token(Request);
                var order=await api.OrderAsync(token,model.Id);model.Order=order;
                if(!CanUpdate(order,model.Section)){TempData["CustomerError"]=UpdateUnavailable(order,model.Section);return RedirectToAction("Details",new{id=model.Id});}
                if(model.Section=="delivery"){
                    if(String.IsNullOrWhiteSpace(model.Recipient)||model.Recipient.Trim().Length<2)ModelState.AddModelError("Recipient","Vui lòng nhập tên người nhận từ 2 ký tự.");
                    if(String.IsNullOrWhiteSpace(model.Phone)||!Regex.IsMatch(model.Phone,@"^\+?[0-9]{9,15}$"))ModelState.AddModelError("Phone","Số điện thoại không hợp lệ.");
                    if(String.IsNullOrWhiteSpace(model.Address)||model.Address.Trim().Length<10)ModelState.AddModelError("Address","Vui lòng nhập địa chỉ nhận hàng từ 10 ký tự.");
                }else{
                    model.OnlineMethods=await api.PaymentOptionsAsync(token);
                    if(String.IsNullOrEmpty(model.PaymentMethod))ModelState.AddModelError("PaymentMethod","Vui lòng chọn phương thức thanh toán.");
                }
                if(ModelState.IsValid){
                    try{
                        await api.UpdateOrderAsync(token,model);
                        TempData["CustomerMessage"]="Đã cập nhật thông tin đơn hàng. Mã đơn và sản phẩm được giữ nguyên.";
                        return RedirectToAction("Details",new{id=model.Id});
                    }catch(CustomerApiException e){if(e.StatusCode==401)return LoginAgain();ModelState.AddModelError("",e.Message);}
                    catch(Exception){ModelState.AddModelError("","Chưa xác nhận được kết quả. Vui lòng xem lại đơn hàng trước khi thử lại.");}
                }
                return View(model);
            }catch(CustomerApiException e){if(e.StatusCode==401)return LoginAgain();if(e.StatusCode==404)return HttpNotFound();return Unavailable();}
            catch(Exception){return Unavailable();}
        }
        private static bool CanUpdate(OrderDetailsModel order,string section){return section=="delivery"?order.CanUpdateDelivery:order.CanUpdatePayment;}
        private static string UpdateUnavailable(OrderDetailsModel order,string section){return section=="payment"&&!String.IsNullOrEmpty(order.PaymentUpdateNotice)?order.PaymentUpdateNotice:
            "Thông tin đơn không còn được phép thay đổi. Vui lòng liên hệ cửa hàng để được hỗ trợ.";}
        private ActionResult LoginAgain() { CustomerAuthentication.SignOut(); return RedirectToAction("Login","Account",new { returnUrl=Url.Action("Index") }); }
        private ActionResult Unavailable() { Response.StatusCode=503;Response.TrySkipIisCustomErrors=true;ViewBag.CustomerError="Không thể tải đơn hàng. Vui lòng thử lại sau.";return View("~/Views/Account/CustomerError.cshtml"); }
    }
}
