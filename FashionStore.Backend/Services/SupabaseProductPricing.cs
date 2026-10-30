using FashionStore.Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FashionStore.Backend.Services;

internal static class SupabaseProductPricing
{
    internal const string ProductSql="""
        SELECT p.masku,p.masp,p.maloai,p.tensanpham,p.kichco,p.mausac,
            price.sale_price AS dongia,p.soluongtonkho,p.trangthai,p.hinhanh
        FROM public.sanpham p JOIN webstore.sku_price price ON price.masku=p.masku
        """;
    internal static Task<bool> Installed(DatabaseFacade database,CancellationToken ct)=>database.IsNpgsql()
        ? database.SqlQueryRaw<bool>("SELECT to_regclass('webstore.sku_price') IS NOT NULL AS \"Value\"").SingleAsync(ct)
        : Task.FromResult(false);
}
