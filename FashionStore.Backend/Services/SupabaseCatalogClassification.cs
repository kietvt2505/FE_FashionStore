using FashionStore.Catalog;

namespace FashionStore.Backend.Services;

internal static class SupabaseCatalogClassification
{
    private static string Literal(string? value) => value == null ? "NULL::text" : "'" + value.Replace("'", "''") + "'";
    private static readonly string Rows = string.Join(",\n", CatalogTaxonomy.Rules.Select(r => $"({Literal(r.Name)},{Literal(r.Audience)},{Literal(r.Kind)})"));

    // Use current, explicit categories before older per-product assignments. Product IDs can be reused
    // when the partner imports a new catalog; a prior dress must not turn a new men's hoodie into a dress.
    internal static readonly string Sql = $$"""
        SELECT p.masp,
          CASE WHEN count(DISTINCT p.maloai)=1 THEN max(COALESCE(t.audience,m.audience)) END AS audience,
          CASE WHEN count(DISTINCT p.maloai)=1 THEN max(COALESCE(t.kind,m.kind)) END AS kind,
          max(m.regular_price) AS regular_price
        FROM public.sanpham p
        JOIN public.loaisanpham c ON c.maloai=p.maloai
        LEFT JOIN webstore.catalog_product m ON m.masp=p.masp
        LEFT JOIN (VALUES {{Rows}}) AS t(name,audience,kind)
          ON t.name=regexp_replace(btrim(lower(replace(translate(c.tenloai,
             'àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđÀÁẠẢÃÂẦẤẬẨẪĂẰẮẶẲẴÈÉẸẺẼÊỀẾỆỂỄÌÍỊỈĨÒÓỌỎÕÔỒỐỘỔỖƠỜỚỢỞỠÙÚỤỦŨƯỪỨỰỬỮỲÝỴỶỸĐ',
             'aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyydAAAAAAAAAAAAAAAAAEEEEEEEEEEEIIIIIOOOOOOOOOOOOOOOOOUUUUUUUUUUUYYYYYD'),'-',' '))), '\s+', ' ', 'g')
        WHERE p.trangthai AND c.trangthai
        GROUP BY p.masp
        """;
}
