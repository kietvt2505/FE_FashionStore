using FashionStore.Backend.Data;
using FashionStore.Backend.DTOs;
using Microsoft.EntityFrameworkCore;

namespace FashionStore.Backend.Services;

internal static class SupabaseNativeProductImages
{
    // WinForms keeps the cover in hinhanh and its ordered gallery in danhsachhinhanh.
    // Reading the optional field through JSON also supports older partner schemas.
    private const string Sql = """
        SELECT p.masp,p.masku,p.mausac AS color,btrim(photo.value #>> ARRAY[]::text[]) AS url
        FROM public.sanpham p
        CROSS JOIN LATERAL jsonb_array_elements(
            CASE WHEN jsonb_typeof(to_jsonb(p)->'danhsachhinhanh')='array'
                THEN to_jsonb(p)->'danhsachhinhanh' ELSE '[]'::jsonb END
        ) WITH ORDINALITY AS photo(value,position)
        WHERE p.masku=ANY({0}) AND jsonb_typeof(photo.value)='string'
            AND btrim(photo.value #>> ARRAY[]::text[])<>''
        ORDER BY p.masku,photo.position
        """;

    internal static Task<List<ImageRow>> ReadAsync(SupabaseCatalogDbContext db,string[] visibleSkus,CancellationToken ct) =>
        visibleSkus.Length>0 && db.Database.IsNpgsql()
            ? db.Database.SqlQueryRaw<ImageRow>(Sql,(object)visibleSkus).ToListAsync(ct)
            : Task.FromResult(new List<ImageRow>());

    internal sealed class ImageRow
    {
        public string masp { get; set; } = "";
        public string masku { get; set; } = "";
        public string? color { get; set; }
        public string url { get; set; } = "";
        public ProductImageResponse ToResponse() => new(masku,url) { Color=color };
    }
}
