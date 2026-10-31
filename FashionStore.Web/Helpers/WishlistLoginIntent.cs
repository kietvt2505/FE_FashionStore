using System;
using System.Linq;

namespace FashionStore.Web.Helpers
{
    [Serializable]
    public sealed class WishlistLoginIntent
    {
        public const string SessionKey = "Yody.PendingWishlist";
        public string ProductId { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }

        public static WishlistLoginIntent Create(string productId, DateTimeOffset now)
        {
            var id = (productId ?? "").Trim();
            if (id.Length == 0 || id.Length > 20 || id.Any(Char.IsControl) || id.IndexOfAny(new[] { '/', '\\' }) >= 0)
                return null;
            return new WishlistLoginIntent { ProductId = id, CreatedAtUtc = now };
        }

        public bool IsCurrent(DateTimeOffset now)
        {
            return !String.IsNullOrWhiteSpace(ProductId) && now >= CreatedAtUtc && now - CreatedAtUtc < TimeSpan.FromMinutes(30);
        }
    }
}
