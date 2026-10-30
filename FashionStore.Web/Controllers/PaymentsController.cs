using System;
using System.Threading.Tasks;
using System.Web.Mvc;
using FashionStore.Web.Helpers;
using FashionStore.Web.Services;

namespace FashionStore.Web.Controllers
{
    [Authorize,OutputCache(NoStore=true,Duration=0,VaryByParam="*")]
    public sealed class PaymentsController:Controller
    {
        [HttpPost,ValidateAntiForgeryToken]
        public async Task<ActionResult> Recheck(string id)
        {
            if(String.IsNullOrWhiteSpace(id)||id.Length>30)return HttpNotFound();
            try{
                var result=await new CustomerApiClient().RecheckPaymentAsync(CustomerAuthentication.Token(Request),id);
                TempData["CustomerMessage"]=(string)result["status"]=="PAID"?"Thanh toán đã được xác nhận.":(string)result["status"]=="REVIEW"?"Cửa hàng đã nhận thông tin thanh toán và cần kiểm tra đơn hàng.":"Chưa có xác nhận thanh toán thành công từ nhà cung cấp.";
            }catch(CustomerApiException e){TempData["CustomerError"]=e.Message;}catch(Exception){TempData["CustomerError"]="Chưa truy vấn được kết quả thanh toán. Vui lòng thử lại sau.";}
            return RedirectToAction("Details","Orders",new{id=id});
        }
        [HttpPost,ValidateAntiForgeryToken]
        public async Task<ActionResult> Start(string id)
        {
            if(String.IsNullOrWhiteSpace(id)||id.Length>30)return HttpNotFound();
            try{return Redirect((await new CustomerApiClient().StartPaymentAsync(CustomerAuthentication.Token(Request),id,Request.UserHostAddress)).Url);}
            catch(CustomerApiException e){if(e.StatusCode==401){CustomerAuthentication.SignOut();return RedirectToAction("Login","Account");}TempData["CustomerError"]=e.Message;}
            catch(Exception){TempData["CustomerError"]="Chưa mở được cổng thanh toán. Vui lòng kiểm tra trạng thái đơn trước khi thử lại.";}
            return RedirectToAction("Details","Orders",new{id=id});
        }
    }
}
