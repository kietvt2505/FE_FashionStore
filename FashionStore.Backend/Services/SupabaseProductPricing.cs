using FashionStore.Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FashionStore.Backend.Services;

internal static class SupabaseProductPricing
{
    internal const string ProductSql="""
        SELECT p.masku,p.masp,p.maloai,p.tensanpham,p.kichco,p.mausac,
            COALESCE(price.giakhuyenmai,p.dongia) AS dongia,p.soluongtonkho,p.trangthai,p.hinhanh
        FROM public.sanpham p LEFT JOIN public.sanphamkhuyenmai_hientai price ON price.masku=p.masku
        """;
    internal static Task<bool> Installed(DatabaseFacade database,CancellationToken ct)=>database.IsNpgsql()
        ? Task.FromResult(true)
        : Task.FromResult(false);
}
