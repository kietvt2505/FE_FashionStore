using System;
using System.Threading.Tasks;
using FashionStore.Web.Models;
using Newtonsoft.Json.Linq;

namespace FashionStore.Web.Services
{
    public sealed class ProductReviewsService
    {
        private readonly CustomerApiClient api;
        public ProductReviewsService() : this(new CustomerApiClient()) { }
        internal ProductReviewsService(CustomerApiClient client) { api = client; }

        public async Task<ProductReviewsViewModel> LoadAsync(string productId, string token, int page)
        {
            var model = new ProductReviewsViewModel
            {
                ProductId = productId, Page = Math.Max(1, Math.Min(page, 100000)),
                RequiresLogin = String.IsNullOrEmpty(token)
            };
            var path = "reviews/" + Uri.EscapeDataString(productId);
            var publicTask = LoadList(path + "?page=" + model.Page, null,
                rows => model.PublicReviews = rows,
                error => model.PublicError = "Chưa thể tải đánh giá. Vui lòng thử lại sau.");
            if (model.RequiresLogin) { await publicTask; return model; }

            await Task.WhenAll(publicTask,
                LoadList(path + "/mine?page=" + model.Page, token,
                    rows => model.MyReviews = rows,
                    error => {
                        if (IsUnauthorized(error)) model.RequiresLogin = true;
                        model.MyReviewsError = "Chưa thể tải đánh giá bạn đã gửi. Vui lòng thử lại sau.";
                    }),
                LoadList(path + "/eligible-orders", token,
                    rows => model.EligibleOrders = rows,
                    error => {
                        if (IsUnauthorized(error)) model.RequiresLogin = true;
                        model.EligibilityError = "Chưa thể kiểm tra đơn đủ điều kiện đánh giá. Vui lòng thử lại sau.";
                    }));
            return model;
        }

        private async Task LoadList(string path, string token, Action<JArray> loaded, Action<Exception> failed)
        {
            try { loaded(await api.FeatureListAsync(token, path)); }
            catch (Exception error) { failed(error); }
        }

        private static bool IsUnauthorized(Exception error)
        {
            var apiError = error as CustomerApiException;
            return apiError != null && apiError.StatusCode == 401;
        }
    }
}
