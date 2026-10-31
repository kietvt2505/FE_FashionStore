using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Mvc;
using FashionStore.Web.Models;
using FashionStore.Web.Services;

namespace FashionStore.Web.Controllers
{
    public class ProductController : Controller
    {
        private const int PageSize = 12;

        [HttpGet]
        public async Task<ActionResult> Index(string q, string audience, string category, string sort, int? page, decimal? minPrice = null, decimal? maxPrice = null, string size = null, string color = null, bool inStock = false, string kind = null, bool saleOnly = false)
        {
            if(String.Equals(audience,"TRE_EM",StringComparison.OrdinalIgnoreCase))return RedirectToAction("Index",new{kind="phu-kien"});
            int pageNumber = Math.Max(1, Math.Min(page ?? 1, 100000));
            if (saleOnly) { audience = null; if (String.IsNullOrWhiteSpace(sort)) sort = "price_asc"; }
            var model = EmptyModel(q, audience, category, sort, pageNumber);
            model.Kind=kind; model.SaleOnly=saleOnly;
            if(!ModelState.IsValid){model.CatalogError="Giá hoặc số trang không hợp lệ.";SetErrorStatus(400);return View(model);}
            try
            {
                var catalog = new CatalogRepository();
                model = await catalog.SearchAsync(q, audience, category, sort, pageNumber, PageSize, minPrice, maxPrice, size, color, inStock, kind, saleOnly);
                model.Categories = FashionStore.Web.Helpers.CatalogViewHelper.CategoriesForContext(await catalog.GetCategoriesAsync(),null,kind);
                ViewBag.CatalogSelectedColor=model.Color;
                Session["ShoppingReturnUrl"]=Request.RawUrl;
            }
            catch (CatalogApiException error)
            {
                Trace.TraceError("Catalog list API: " + error);
                SetErrorStatus(error.StatusCode == 400 ? 400 : 503);
                model.CatalogError = error.StatusCode == 400
                    ? "Bộ lọc không hợp lệ. Vui lòng kiểm tra lại tìm kiếm và lựa chọn."
                    : "Chưa thể tải danh sách sản phẩm. Vui lòng thử lại sau.";
            }
            catch (Exception error)
            {
                Trace.TraceError("Catalog list: " + error);
                SetErrorStatus(503);
                model.CatalogError = "Chưa thể tải danh sách sản phẩm. Vui lòng thử lại sau.";
            }
            return View(model);
        }

        [ChildActionOnly]
        public ActionResult Navigation()
        {
            var categories = System.Web.HttpRuntime.Cache["catalog-nav-categories-v2"] as System.Collections.Generic.IList<CategoryOption>;
            if (categories == null) {
                try {
                    categories = new CatalogRepository().GetCategoriesAsync().GetAwaiter().GetResult();
                    System.Web.HttpRuntime.Cache.Insert("catalog-nav-categories-v2", categories, null, DateTime.UtcNow.AddMinutes(1), System.Web.Caching.Cache.NoSlidingExpiration);
                } catch (Exception) { categories = new System.Collections.Generic.List<CategoryOption>(); }
            }
            return PartialView("_CatalogNavigation", categories);
        }

        [HttpGet]
        public async Task<ActionResult> Details(string id,string returnUrl=null,string color=null)
        {
            if(Url.IsLocalUrl(returnUrl)&&returnUrl.StartsWith("/Product",StringComparison.OrdinalIgnoreCase))Session["ShoppingReturnUrl"]=returnUrl;
            ViewBag.ShoppingReturnUrl=Session["ShoppingReturnUrl"] as string??Url.Action("Index","Product");
            if (String.IsNullOrWhiteSpace(id) || id.Length > 20) return HttpNotFound();
            try
            {
                var model = await new CatalogRepository().GetDetailAsync(id);
                if (model.Product == null) return HttpNotFound();
                model.SelectedColor=FashionStore.Web.Helpers.CatalogViewHelper.InitialColor(model.Variants,color);
                try {
                    var related = await new CatalogRepository().SearchAsync(null,null,model.Product.MaLoai,"new",1,5);
                    model.Related = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Take(System.Linq.Enumerable.Where(related.Products, p => p.MaSP != id),4));
                    var accessories=await new CatalogRepository().SearchAsync(null,null,null,"new",1,5,kind:"phu-kien",inStock:true);
                    model.Accessories=System.Linq.Enumerable.ToList(System.Linq.Enumerable.Take(System.Linq.Enumerable.Where(accessories.Products,p=>p.MaSP!=id),4));
                } catch(Exception) { /* Related content must not hide the primary product. */ }
                return View(model);
            }
            catch (Exception error)
            {
                Trace.TraceError("Catalog detail: " + error);
                SetErrorStatus(503);
                return View(new ProductDetailViewModel
                {
                    CatalogError = "Chưa thể tải sản phẩm. Vui lòng thử lại sau."
                });
            }
        }

        private void SetErrorStatus(int status)
        {
            Response.StatusCode = status;
            Response.TrySkipIisCustomErrors = true;
        }

        private static CatalogListViewModel EmptyModel(string q, string audience, string category, string sort, int page)
        {
            return new CatalogListViewModel
            {
                Query = q ?? "", Audience = audience ?? "", CategoryId = category ?? "",
                Sort = String.IsNullOrWhiteSpace(sort) ? "new" : sort, Page = page, PageSize = PageSize
            };
        }
    }
}
