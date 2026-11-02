using System.Net;
using System.Text;
using FashionStore.Web.Services;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class ProductReviewsServiceTests
{
    [Fact]
    public async Task GuestCanReadReviewsWithoutRequestingPrivateOrdersOrReviews()
    {
        var handler = new ReviewHandler();
        using var http = new HttpClient(handler);
        var model = await new ProductReviewsService(new CustomerApiClient(http, "http://backend.test/"))
            .LoadAsync("SP-086", null, 2);

        Assert.True(model.RequiresLogin);
        Assert.Single(model.PublicReviews);
        Assert.Empty(model.EligibleOrders);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/customer/reviews/SP-086?page=2", request.Path);
        Assert.Null(request.Token);
    }

    [Fact]
    public async Task AuthenticatedPageLoadsOwnReviewsAndServerEligibleOrdersWithItsToken()
    {
        var handler = new ReviewHandler();
        using var http = new HttpClient(handler);
        var model = await new ProductReviewsService(new CustomerApiClient(http, "http://backend.test/"))
            .LoadAsync("SP-086", "customer-token", 1);

        Assert.False(model.RequiresLogin);
        Assert.Single(model.PublicReviews);
        Assert.Equal("HD-DELIVERED", (string?)Assert.Single(model.EligibleOrders)["SoHD"]);
        Assert.Equal("HIEN_THI", (string?)Assert.Single(model.MyReviews)["TrangThai"]);
        Assert.All(handler.Requests.Where(r => r.Path.Contains("/mine") || r.Path.EndsWith("/eligible-orders")),
            request => Assert.Equal("customer-token", request.Token));
        Assert.Null(handler.Requests.Single(r => r.Path == "/api/customer/reviews/SP-086?page=1").Token);
    }

    [Fact]
    public async Task FailedPublicListStillLoadsOwnReviewAndEligibleOrder()
    {
        var handler = new ReviewHandler { PublicStatus = HttpStatusCode.ServiceUnavailable };
        using var http = new HttpClient(handler);
        var model = await new ProductReviewsService(new CustomerApiClient(http, "http://backend.test/"))
            .LoadAsync("SP-086", "customer-token", 1);

        Assert.NotNull(model.PublicError);
        Assert.Single(model.MyReviews);
        Assert.Single(model.EligibleOrders);
        Assert.Null(model.EligibilityError);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    public async Task FailedEligibilityNeverOffersAnUnverifiedReviewForm(HttpStatusCode status, bool requiresLogin)
    {
        var handler = new ReviewHandler { EligibilityStatus = status };
        using var http = new HttpClient(handler);
        var model = await new ProductReviewsService(new CustomerApiClient(http, "http://backend.test/"))
            .LoadAsync("SP-086", "customer-token", 1);

        Assert.Empty(model.EligibleOrders);
        Assert.NotNull(model.EligibilityError);
        Assert.Single(model.PublicReviews);
        Assert.Equal(requiresLogin, model.RequiresLogin);
    }

    private sealed class ReviewHandler : HttpMessageHandler
    {
        public List<(string Path, string? Token)> Requests { get; } = [];
        public HttpStatusCode PublicStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode EligibilityStatus { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((path, request.Headers.Authorization?.Parameter));
            var eligible = path.EndsWith("/eligible-orders");
            var mine = path.Contains("/mine");
            var status = eligible ? EligibilityStatus : mine ? HttpStatusCode.OK : PublicStatus;
            var body = eligible ? "[{\"SoHD\":\"HD-DELIVERED\"}]" : mine
                ? "[{\"SoSao\":5,\"TrangThai\":\"HIEN_THI\"}]" : "[{\"SoSao\":5,\"NoiDung\":\"Trải nghiệm đã nhận hàng\"}]";
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK ? body : "{\"title\":\"Unavailable\"}", Encoding.UTF8, "application/json")
            });
        }
    }
}
