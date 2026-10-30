using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Mvc;
using FashionStore.Web.Models;
using FashionStore.Web.Services;

namespace FashionStore.Web.Controllers
{
    public class HomeController : Controller
    {
        public async Task<ActionResult> Index()
        {
            var model = new HomeViewModel();
            CatalogRepository catalog;
            try { catalog = new CatalogRepository(); }
            catch (Exception error) {
                Trace.TraceError("Catalog home configuration: " + error);
                Response.StatusCode = 503;
                Response.TrySkipIisCustomErrors = true;
                model.CatalogError = "Chưa thể tải danh sách sản phẩm. Vui lòng thử lại sau.";
                model.SaleError = "Chưa thể tải sản phẩm ưu đãi. Vui lòng thử lại sau.";
                return View(model);
            }
            var featuredTask = catalog.GetFeaturedAsync(8);
            var saleTask = catalog.SearchAsync(null, null, null, "price_asc", 1, 4, saleOnly: true);
            try { model.FeaturedProducts = await featuredTask; }
            catch (Exception error)
            {
                Trace.TraceError("Catalog home: " + error);
                Response.StatusCode = 503;
                Response.TrySkipIisCustomErrors = true;
                model.CatalogError = "Chưa thể tải danh sách sản phẩm. Vui lòng thử lại sau.";
            }
            try { model.SaleProducts = (await saleTask).Products; }
            catch (Exception error) {
                Trace.TraceError("Catalog home promotions: " + error);
                model.SaleError = "Chưa thể tải sản phẩm ưu đãi. Vui lòng thử lại sau.";
            }
            return View(model);
        }

        public ActionResult Policy(string id) {
            var allowed=new[]{"shopping","shipping","payment","returns","privacy","terms","faq"};
            if(Array.IndexOf(allowed,id)<0)return HttpNotFound();ViewBag.Policy=id;return View();
        }
        public ActionResult About() { return View(); }
        public ActionResult Contact() { return View(); }
    }
}
