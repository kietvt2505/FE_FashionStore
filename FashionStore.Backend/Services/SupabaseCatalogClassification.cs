using FashionStore.Catalog;

namespace FashionStore.Backend.Services;

internal static class SupabaseCatalogClassification
{
    private static string Literal(string? value) => value == null ? "NULL::text" : "'" + value.Replace("'", "''") + "'";
    private static readonly string Rows = string.Join(",\n", CatalogTaxonomy.Rules.Select(r => $"({Literal(r.Name)},{Literal(r.Audience)},{Literal(r.Kind)})"));

    // Use current, explicit categories before older per-product assignments. Product IDs can be reused
    // when the partner imports a new catalog; a prior dress must not turn a new men's hoodie into a dress.
    internal static string ForSchema(bool supplemental) => $$"""
        SELECT p.masp,
          CASE WHEN count(DISTINCT p.maloai)=1 THEN max(COALESCE(
            CASE WHEN to_jsonb(p)->>'danhmuc' IN ('NAM','NU','TRE_EM','UNISEX') THEN to_jsonb(p)->>'danhmuc'
                 WHEN to_jsonb(p)->>'danhmuc'='PHU_KIEN' THEN 'UNISEX' END,
            t.audience,{{(supplemental?"m.audience":"NULL::text")}})) END AS audience,
          CASE WHEN count(DISTINCT p.maloai)=1 THEN max(COALESCE(t.kind,{{(supplemental?"m.kind":"NULL::text")}})) END AS kind,
          {{(supplemental?"max(m.regular_price)":"NULL::numeric")}} AS regular_price
        FROM public.sanpham p
        JOIN public.loaisanpham c ON c.maloai=p.maloai
        {{(supplemental?"LEFT JOIN public.thongtinsanpham m ON m.masp=p.masp":"")}}
        LEFT JOIN (VALUES {{Rows}}) AS t(name,audience,kind)
          ON t.name=regexp_replace(btrim(lower(replace(translate(c.tenloai,
             'àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđÀÁẠẢÃÂẦẤẬẨẪĂẰẮẶẲẴÈÉẸẺẼÊỀẾỆỂỄÌÍỊỈĨÒÓỌỎÕÔỒỐỘỔỖƠỜỚỢỞỠÙÚỤỦŨƯỪỨỰỬỮỲÝỴỶỸĐ',
             'aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyydAAAAAAAAAAAAAAAAAEEEEEEEEEEEIIIIIOOOOOOOOOOOOOOOOOUUUUUUUUUUUYYYYYD'),'-',' '))), '\s+', ' ', 'g')
        WHERE p.trangthai AND c.trangthai
        GROUP BY p.masp
        """;
}
