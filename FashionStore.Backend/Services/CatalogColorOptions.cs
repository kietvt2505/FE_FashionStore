using FashionStore.Backend.DTOs;

namespace FashionStore.Backend.Services;

internal static class CatalogColorOptions
{
    internal static IReadOnlyList<ProductColorResponse> Create(IEnumerable<VariantResponse> variants,IEnumerable<ProductImageResponse> images,bool preferDiscounted=false)
    {
        var photos=images.Where(i=>!string.IsNullOrWhiteSpace(i.Url)).ToList();
        return variants.Where(v=>!string.IsNullOrWhiteSpace(v.Color))
            .GroupBy(v=>v.Color.Trim(),StringComparer.OrdinalIgnoreCase)
            .Select(group=>{
                var candidates=preferDiscounted&&group.Any(v=>v.OriginalPrice>v.Price)?group.Where(v=>v.OriginalPrice>v.Price):group;
                var priced=candidates.OrderBy(v=>v.Price).ThenBy(v=>v.Sku,StringComparer.Ordinal).First();
                var skus=group.Select(v=>v.Sku).ToHashSet(StringComparer.Ordinal);
                var photo=photos.FirstOrDefault(i=>String.Equals(i.Color?.Trim(),group.Key,StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(i.Color) && i.Sku!=null && skus.Contains(i.Sku))
                    ?? photos.FirstOrDefault(i=>string.IsNullOrWhiteSpace(i.Color)&&string.IsNullOrEmpty(i.Sku));
                return new ProductColorResponse(group.Key,photo?.Url,priced.Price,group.Sum(v=>(long)v.Stock)){OriginalPrice=priced.OriginalPrice};
            }).ToList();
    }
}
