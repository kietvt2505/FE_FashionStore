using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FashionStore.Web.Models;
using FashionStore.Catalog;

namespace FashionStore.Web.Helpers
{
    public static class CatalogViewHelper
    {
        public sealed class MenuEntry { public string Label { get; set; } public string Kind { get; set; } public string Category { get; set; } }
        public sealed class MenuSection { public string Title { get; set; } public string Kind { get; set; } public List<MenuEntry> Items { get; set; } = new List<MenuEntry>(); }
        private static string Normalize(string text) { return new string((text ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('đ','d'); }
        public static IList<MenuSection> MenuSections(string audience, IList<CategoryOption> categories)
        {
            var groups=new Dictionary<string,MenuSection> {
                {"shirts",new MenuSection{Title="Áo"}}, {"bottoms",new MenuSection{Title=audience=="NU"?"Quần / Váy":"Quần"}},
                {"clothing",new MenuSection{Title="Trang phục khác"}}, {"footwear",new MenuSection{Title="Giày dép"}},
                {"accessories",new MenuSection{Title="Phụ kiện",Kind="phu-kien"}}, {"other",new MenuSection{Title="Danh mục khác"}}
            };
            foreach(var category in CategoriesForContext(categories,audience,null).GroupBy(c=>c.MaLoai).Select(g=>g.First()))
                groups[CatalogTaxonomy.Section(category.TenLoai)].Items.Add(new MenuEntry{Label=category.TenLoai,Category=category.MaLoai});
            return groups.Values.Where(g=>g.Items.Count>0).ToList();
        }
        public static IList<CategoryOption> CategoriesForContext(IList<CategoryOption> categories,string audience,string kind)
        {
            return categories.Where(c=>CatalogTaxonomy.Normalize(c.TenLoai)!="123"&&CatalogTaxonomy.MatchesAudience(c.TenLoai,audience))
                .Where(c=>String.IsNullOrEmpty(kind)||(kind=="phu-kien"?CatalogTaxonomy.Section(c.TenLoai)=="accessories":CatalogTaxonomy.Find(c.TenLoai)!=null&&CatalogTaxonomy.Find(c.TenLoai).Kind==kind)).ToList();
        }
        public static string KindLabel(string kind)
        {
            return CatalogTaxonomy.KindLabel(kind);
        }
        public static bool IsAccessoryKind(string kind) { return CatalogTaxonomy.AccessoryKinds.Contains(kind); }
        public static string ColorCss(string color)
        {
            var value=Normalize(color).Trim();
            var colors=new Dictionary<string,string>{{"den","#202020"},{"trang","#fff"},{"do","#b7393b"},{"xam","#929496"},{"xanh duong","#4775ae"},{"xanh navy","#172d4d"},{"xanh la","#44735a"},{"xanh dam","#244a53"},{"vang","#deb94d"},{"cam","#d87d41"},{"hong","#d49ba7"},{"tim","#8b72a7"},{"nau","#8a6348"},{"be","#d5c4a7"}};
            return colors.ContainsKey(value)?colors[value]:"repeating-linear-gradient(45deg,#ddd 0 4px,#aaa 4px 8px)";
        }
        public static IEnumerable<string> OrderedSizes(IEnumerable<string> sizes)
        {
            var order=new[]{"XS","S","M","L","XL","XXL","XXXL"};
            return sizes.Distinct().OrderBy(s=>Array.IndexOf(order,s)<0?99:Array.IndexOf(order,s)).ThenBy(s=>s);
        }

        private const string Placeholder = "~/Content/images/product-placeholder.svg";

        public static string PhotoPath(string raw)
        {
            var path=ImagePath(raw);
            return path==Placeholder?null:path;
        }

        public static string InitialColor(IEnumerable<ProductVariant> variants,string requested)
        {
            var choices=variants.ToList();
            var match=choices.FirstOrDefault(v=>!String.IsNullOrWhiteSpace(requested)&&String.Equals(v.MauSac==null?null:v.MauSac.Trim(),requested.Trim(),StringComparison.OrdinalIgnoreCase));
            return match!=null?match.MauSac:choices.Where(v=>v.SoLuongTonKho>0).Select(v=>v.MauSac).FirstOrDefault()??choices.Select(v=>v.MauSac).FirstOrDefault()??"";
        }

        public static IList<ProductImage> ColorPhotos(ProductDetailViewModel model,string color)
        {
            return model.Images.Where(image=>{
                var effective=image.MauSac??model.Variants.Where(v=>v.MaSKU==image.MaSKU).Select(v=>v.MauSac).FirstOrDefault();
                var shared=String.IsNullOrWhiteSpace(image.MauSac)&&String.IsNullOrWhiteSpace(image.MaSKU);
                return PhotoPath(image.DuongDan)!=null&&(shared||String.Equals(effective==null?null:effective.Trim(),color==null?null:color.Trim(),StringComparison.OrdinalIgnoreCase));
            }).GroupBy(image=>PhotoPath(image.DuongDan),StringComparer.Ordinal).Select(group=>group.First()).ToList();
        }

        public static string ImagePath(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return Placeholder;
            string value = raw.Trim().Replace('\\', '/');
            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri))
                return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? value : Placeholder;
            if (value.Contains("..") || value.Contains(":") || value.StartsWith("//")) return Placeholder;
            if (value.StartsWith("~/") || value.StartsWith("/")) return value;
            return "~/" + value.TrimStart('/');
        }

        public static string Vnd(decimal price)
        {
            return price.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " ₫";
        }

        public static string AudienceLabel(string value)
        {
            switch (value)
            {
                case "NAM": return "Nam";
                case "NU": return "Nữ";
                case "TRE_EM": return "Trẻ em";
                case "UNISEX": return "Nam và nữ";
                default: return "Thời trang";
            }
        }
    }
}
