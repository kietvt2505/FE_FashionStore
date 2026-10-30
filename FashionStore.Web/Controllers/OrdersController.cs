using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
            model.RequiresReason=!accountOnly&&order.PaymentMethod!="COD";
            model.RequiresRefund=accountOnly||order.PaymentStatus=="DA_THANH_TOAN";
            model.RefundAccountOnly=accountOnly;
            if(accountOnly?order.Refund==null||order.Refund.Status!="CHO_THONG_TIN":!order.CanCancel)
                ModelState.AddModelError("","Trạng thái đơn đã thay đổi. Vui lòng tải lại trang để xem kết quả mới nhất.");
            if(model.RequiresReason&&(String.IsNullOrWhiteSpace(model.Reason)||model.Reason.Trim().Length<5))
                ModelState.AddModelError("Reason","Vui lòng nhập lý do hủy từ 5–500 ký tự.");
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
        private ActionResult LoginAgain() { CustomerAuthentication.SignOut(); return RedirectToAction("Login","Account",new { returnUrl=Url.Action("Index") }); }
        private ActionResult Unavailable() { Response.StatusCode=503;Response.TrySkipIisCustomErrors=true;ViewBag.CustomerError="Không thể tải đơn hàng. Vui lòng thử lại sau.";return View("~/Views/Account/CustomerError.cshtml"); }
    }
}
