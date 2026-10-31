using FashionStore.Backend.Data;
using FashionStore.Backend.DTOs;
using FashionStore.Backend.Middleware;
using Microsoft.EntityFrameworkCore;
using FashionStore.Catalog;

namespace FashionStore.Backend.Services;

public sealed class SupabaseCatalogService(SupabaseCatalogDbContext db) : ICatalogService
{
    private bool nativePricing;
    private IQueryable<CatalogMerchandising> Classifications() => db.Database.IsRelational()
        ? db.Merchandising.FromSqlRaw(SupabaseCatalogClassification.Sql).AsNoTracking() : db.Merchandising.AsNoTracking();
    private IQueryable<SupabaseProduct> Visible() => (nativePricing ? db.Products.FromSqlRaw(SupabaseProductPricing.ProductSql) : db.Products).AsNoTracking()
        .Where(p => p.trangthai && p.Category.trangthai &&
            p.tensanpham.Trim() != "123" && p.Category.tenloai.Trim() != "123");

    private Task<bool> MetadataAvailable(CancellationToken ct) => db.Database.IsRelational()
        ? db.Database.SqlQueryRaw<bool>("SELECT to_regclass('webstore.catalog_product') IS NOT NULL AS \"Value\"").SingleAsync(ct)
        : Task.FromResult(true);

    internal IQueryable<SupabaseProduct> Filter(ProductQuery query, bool metadata = true)
    {
        var products = Visible();
        if (!metadata && (!string.IsNullOrEmpty(query.Audience) || !string.IsNullOrEmpty(query.Kind) || (query.SaleOnly && !nativePricing)))
            return products.Where(p => false);
        var classifications=Classifications();
        if (!query.SaleOnly && !string.IsNullOrEmpty(query.Audience))
            products = products.Where(p => classifications.Any(m => m.masp == p.masp && (m.audience == query.Audience ||
                (m.audience == "UNISEX" && (query.Audience == "NAM" || query.Audience == "NU")))));
        if (!string.IsNullOrEmpty(query.Kind))
            products = query.Kind == "phu-kien"
                ? products.Where(p => classifications.Any(m => m.masp == p.masp && CatalogTaxonomy.AccessoryKinds.Contains(m.kind!)))
                : products.Where(p => classifications.Any(m => m.masp == p.masp && m.kind == query.Kind));
        if (query.SaleOnly)
            products = nativePricing ? products.Where(p=>db.Database.SqlQueryRaw<SkuPrice>("SELECT masku,base_price FROM webstore.sku_price").Any(s=>s.masku==p.masku && s.base_price>p.dongia))
                : products.Where(p => db.Merchandising.Any(m => m.masp == p.masp && m.regular_price > p.dongia));
        var text = query.Q?.Trim();
        if (!string.IsNullOrEmpty(text))
            products = products.Where(p => p.tensanpham.ToLower().Contains(text.ToLower()) || p.masp.ToLower().Contains(text.ToLower()));
        if (!string.IsNullOrEmpty(query.Category)) products = products.Where(p => p.maloai == query.Category);
        if (!string.IsNullOrEmpty(query.Size)) products = products.Where(p => p.kichco == query.Size);
        if (!string.IsNullOrEmpty(query.Color)) products = products.Where(p => p.mausac == query.Color);
        if (query.InStock) products = products.Where(p => p.soluongtonkho > 0);
        if (query.MinPrice.HasValue) products = products.Where(p => p.dongia >= query.MinPrice.Value);
        if (query.MaxPrice.HasValue) products = products.Where(p => p.dongia <= query.MaxPrice.Value);
        return products;
    }

    internal sealed class ProductGroup
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public decimal Price { get; init; }
    }

    internal IQueryable<ProductGroup> Page(ProductQuery query, bool metadata = true)
    {
        // One SKU must satisfy every filter. Aggregate all visible SKUs for matching products,
        // matching the existing SQL Server catalog's price/stock semantics.
        var matchingIds = Filter(query, metadata).Select(p => p.masp).Distinct();
        var productRows=Visible().Where(p => matchingIds.Contains(p.masp));
        if(query.SaleOnly && nativePricing)productRows=productRows.Where(p=>db.Database.SqlQueryRaw<SkuPrice>("SELECT masku,base_price FROM webstore.sku_price").Any(s=>s.masku==p.masku && s.base_price>p.dongia));
        var products = productRows
            .GroupBy(p => p.masp).Select(g => new ProductGroup
            {
                Id = g.Key, Name = g.Min(p => p.tensanpham)!, Price = g.Min(p => p.dongia)
            });
        // No creation timestamp exists in the partner schema; use stable ID order.
        var sorted = query.Sort switch
        {
            "price_asc" => products.OrderBy(p => p.Price).ThenBy(p => p.Id),
            "price_desc" => products.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
            "name" => products.OrderBy(p => p.Name).ThenBy(p => p.Id),
            _ => products.OrderBy(p => p.Id)
        };
        return sorted.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);
    }

    public async Task<ProductListResponse> SearchAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        nativePricing=await SupabaseProductPricing.Installed(db.Database,cancellationToken);
        var metadata = await MetadataAvailable(cancellationToken);
        var facets=Filter(new ProductQuery{Audience=query.Audience,Kind=query.Kind,Category=query.Category,Q=query.Q,SaleOnly=query.SaleOnly},metadata);
        var sizes=await facets.Where(p=>p.kichco!=null && p.kichco!="").Select(p=>p.kichco!).Distinct().OrderBy(s=>s).ToListAsync(cancellationToken);
        var colors=await facets.Where(p=>p.mausac!=null && p.mausac!="").Select(p=>p.mausac!).Distinct().OrderBy(s=>s).ToListAsync(cancellationToken);
        var total = await Filter(query, metadata).Select(p => p.masp).Distinct().CountAsync(cancellationToken);
        var page = await Page(query, metadata).ToListAsync(cancellationToken);
        var ids = page.Select(p => p.Id).ToArray();
        var rows = await Visible().Where(p => ids.Contains(p.masp)).Include(p => p.Category).ToListAsync(cancellationToken);
        var annotations = metadata ? await Classifications().Where(m => ids.Contains(m.masp)).ToDictionaryAsync(m => m.masp, cancellationToken) : new Dictionary<string, CatalogMerchandising>();
        var bases=nativePricing?await db.Database.SqlQueryRaw<SkuPrice>("SELECT masku,base_price FROM webstore.sku_price WHERE masku=ANY({0})",(object)rows.Select(r=>r.masku).ToArray()).ToDictionaryAsync(p=>p.masku,p=>p.base_price,cancellationToken):null;
        var media=new List<MediaRow>();
        if(ids.Length>0 && db.Database.IsRelational() && await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('webstore.product_media') IS NOT NULL AS \"Value\"").SingleAsync(cancellationToken))
            media=await db.Database.SqlQueryRaw<MediaRow>("SELECT masp,color,url,alt FROM webstore.product_media WHERE masp=ANY({0}) AND active ORDER BY sort_order,id",(object)ids).ToListAsync(cancellationToken);
        var groups = rows.GroupBy(p => p.masp).ToDictionary(g => g.Key, g => Map(g, annotations.GetValueOrDefault(g.Key),extra:media.Where(m=>m.masp==g.Key).Select(m=>new ProductImageResponse(null,m.url){Color=m.color,Alt=m.alt}),bases:bases,saleOnly:query.SaleOnly));
        var items = page.Where(p => groups.ContainsKey(p.Id)).Select(p => (ProductSummaryResponse)groups[p.Id]).ToList();
        string? notice = null;
        if (!metadata && (!string.IsNullOrEmpty(query.Audience) || !string.IsNullOrEmpty(query.Kind) || query.SaleOnly))
            notice = "Bộ sưu tập đang được cập nhật. Vui lòng quay lại sau.";
        else if (metadata && total == 0 && !query.SaleOnly && !string.IsNullOrEmpty(query.Audience) &&
            !await Filter(new ProductQuery { Audience = query.Audience }, metadata).AnyAsync(cancellationToken))
            notice = "Bộ sưu tập này chưa có sản phẩm được phân loại. Bạn có thể xem tất cả sản phẩm trong lúc cửa hàng cập nhật.";
        return new ProductListResponse(items, total, query.Page, query.PageSize)
        { SupportsAudience = metadata, SupportsNewest = false, Notice = notice,Sizes=sizes,Colors=colors };
    }

    public Task<List<CategoryResponse>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        db.Categories.AsNoTracking().Where(c => c.trangthai && c.tenloai.Trim() != "123").OrderBy(c => c.tenloai).ThenBy(c => c.maloai)
            .Select(c => new CategoryResponse(c.maloai, c.tenloai)).ToListAsync(cancellationToken);

    public async Task<ProductDetailResponse?> GetDetailAsync(string id, CancellationToken cancellationToken)
    {
        nativePricing=await SupabaseProductPricing.Installed(db.Database,cancellationToken);
        var rows = await Visible().Include(p => p.Category).Where(p => p.masp == id).ToListAsync(cancellationToken);
        if (rows.Count == 0) return null;
        var annotation = await MetadataAvailable(cancellationToken) ? await Classifications().SingleOrDefaultAsync(m => m.masp == id, cancellationToken) : null;
        string? description=null;var extra=new List<ProductImageResponse>();var bases=new Dictionary<string,decimal>();
        var enhanced=db.Database.IsRelational() && await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('webstore.product_media') IS NOT NULL AS \"Value\"").SingleAsync(cancellationToken);
        if(enhanced) {
            description=await db.Database.SqlQueryRaw<string>("SELECT COALESCE(description,'') AS \"Value\" FROM webstore.catalog_product WHERE masp={0}",id).SingleOrDefaultAsync(cancellationToken);
            var media=await db.Database.SqlQueryRaw<MediaRow>("SELECT masp,color,url,alt FROM webstore.product_media WHERE masp={0} AND active ORDER BY sort_order,id",id).ToListAsync(cancellationToken);
            extra=media.Select(m=>new ProductImageResponse(null,m.url){Color=m.color,Alt=m.alt}).ToList();
            var prices=await db.Database.SqlQueryRaw<DiscountRow>("SELECT masku,base_price FROM webstore.product_discount_price WHERE masp={0} AND percent>0",id).ToListAsync(cancellationToken);
            bases=prices.ToDictionary(p=>p.masku,p=>p.base_price);
        }
        if(nativePricing)bases=await db.Database.SqlQueryRaw<SkuPrice>("SELECT masku,base_price FROM webstore.sku_price WHERE masku=ANY({0})",(object)rows.Select(r=>r.masku).ToArray()).ToDictionaryAsync(p=>p.masku,p=>p.base_price,cancellationToken);
        return Map(rows, annotation,description,extra,bases);
    }
    private sealed class MediaRow {public string masp{get;set;}="";public string? color{get;set;} public string url{get;set;}="";public string? alt{get;set;}}
    private sealed class DiscountRow {public string masku{get;set;}="";public decimal base_price{get;set;}}
    private sealed class SkuPrice {public string masku{get;set;}="";public decimal base_price{get;set;}}

    public async Task<List<VariantResponse>?> GetVariantsAsync(string id, CancellationToken cancellationToken)
    {
        nativePricing=await SupabaseProductPricing.Installed(db.Database,cancellationToken);
        var rows = await Visible().Where(p => p.masp == id).OrderBy(p => p.masku).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : rows.Select(Variant).ToList();
    }

    private static VariantResponse Variant(SupabaseProduct p) =>
        new(p.masku, p.kichco ?? "", p.mausac ?? "", p.dongia, p.soluongtonkho);

    internal static ProductDetailResponse Map(IEnumerable<SupabaseProduct> source, CatalogMerchandising? metadata = null,string? description=null,IEnumerable<ProductImageResponse>? extra=null,IReadOnlyDictionary<string,decimal>? bases=null,bool saleOnly=false)
    {
        var rows = source.OrderBy(p => p.masku, StringComparer.Ordinal).ToList();
        var first = rows.First();
        var priced=rows.OrderBy(p=>p.dongia).ThenBy(p=>p.masku,StringComparer.Ordinal).First();
        if(saleOnly && bases!=null)priced=rows.Where(p=>bases.TryGetValue(p.masku,out var b)&&b>p.dongia).OrderBy(p=>p.dongia).FirstOrDefault()??priced;
        var original=bases!=null && bases.TryGetValue(priced.masku,out var basePrice)?(basePrice>priced.dongia?basePrice:(decimal?)null):metadata?.regular_price>priced.dongia?metadata.regular_price:null;
        var images = rows.Where(p => !string.IsNullOrWhiteSpace(p.hinhanh))
            .Select(p => new ProductImageResponse(p.masku, p.hinhanh!){Color=p.mausac}).Concat(extra??[]).DistinctBy(i=>(i.Color,i.Url)).ToList();
        var variants=rows.Select(p=>Variant(p) with {OriginalPrice=bases!=null && bases.TryGetValue(p.masku,out var price)?(price>p.dongia?price:(decimal?)null):metadata?.regular_price>p.dongia?metadata.regular_price:null}).ToList();
        return new ProductDetailResponse
        {
            Id = first.masp, Name = first.tensanpham,
            Category = new CategoryResponse(first.maloai, first.Category.tenloai),
            Price = priced.dongia, OriginalPrice = original, Stock = rows.Sum(p => (long)p.soluongtonkho),
            ImageUrl = saleOnly && !string.IsNullOrWhiteSpace(priced.hinhanh)?priced.hinhanh:images.FirstOrDefault()?.Url,
            CreatedAt = null, Audience = metadata?.audience, Kind = metadata?.kind, Description = description,
            Variants=variants,Images=images,ColorOptions=CatalogColorOptions.Create(variants,images,saleOnly)
        };
    }
}
