using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FashionStore.Web.Models;

namespace FashionStore.Web.Helpers
{
    public static class CatalogViewHelper
    {
        public sealed class MenuEntry { public string Label { get; set; } public string Kind { get; set; } public string Category { get; set; } }
        public sealed class MenuSection { public string Title { get; set; } public string Kind { get; set; } public List<MenuEntry> Items { get; set; } = new List<MenuEntry>(); }
        private static string Normalize(string text) { return new string((text ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('đ','d'); }
        public static IList<MenuSection> MenuSections(string audience, IList<CategoryOption> categories)
        {
            Func<string,string,MenuEntry> entry = (label, kind) => {
                var category = categories.FirstOrDefault(c => Normalize(c.TenLoai) == Normalize(label));
                return new MenuEntry { Label=label, Kind=kind, Category=category == null ? null : category.MaLoai };
            };
            var shirts = new MenuSection { Title="Áo", Items=new List<MenuEntry> { entry("Áo thun","ao-thun"), entry("Áo polo","ao-polo"), entry("Áo sơ mi","ao-so-mi"), entry("Áo khoác","ao-khoac"), entry("Áo hoodie","ao-hoodie"), entry("Áo vest","ao-vest") } };
            var pants = new MenuSection { Title=audience == "NU" ? "Quần / Váy" : "Quần", Items=new List<MenuEntry> { entry("Quần ngắn","quan-ngan"), entry("Quần dài","quan-dai"), entry("Quần jogger","quan-jogger") } };
            if (audience == "NU") pants.Items.AddRange(new[]{entry("Váy ngắn","vay-ngan"),entry("Váy dài","vay-dai"),entry("Đầm ngắn","dam-ngan"),entry("Đầm dài","dam-dai")});
            var footwear = new MenuSection { Title="Giày dép", Items=new List<MenuEntry> { entry("Giày","giay"), entry("Dép","dep") } };
            var accessories = new MenuSection { Title="Phụ kiện", Kind="phu-kien", Items=new List<MenuEntry> { entry("Kính mát","kinh-mat"), entry("Mũ","mu"), entry("Vớ","vo"), entry("Dây nịt","day-nit"), entry("Balo","balo") } };
            if (audience == "NU") accessories.Items.Add(entry("Túi xách","tui-xach"));
            var groups = new[] { shirts, pants, footwear, accessories };
            foreach (var category in categories) {
                var normalized=Normalize(category.TenLoai);
                if (normalized.Trim() == "123") continue;
                // Match whole gender words after normalizing case and Vietnamese accents.
                var male=Regex.IsMatch(normalized,@"\bnam\b");
                var female=Regex.IsMatch(normalized,@"\bnu\b");
                if (audience == "NAM" && female && !male) continue;
                if (audience == "NU" && male && !female) continue;
                if (audience == "NAM" && Regex.IsMatch(normalized,@"\bdam\b")) continue;
                if (audience != "NU" && (normalized.Contains("vay") || normalized.Contains("tui"))) continue;
                if (groups.SelectMany(g=>g.Items).Any(e=>e.Category == category.MaLoai)) continue;
                var target=normalized.Contains("quan") || normalized.Contains("vay") || normalized.Contains("dam") ? pants : normalized.Contains("giay") || normalized.Contains("dep") ? footwear : normalized.Contains("ao") && !normalized.Contains("balo") ? shirts : accessories;
                target.Items.Add(new MenuEntry { Label=category.TenLoai, Category=category.MaLoai });
            }
            return groups;
        }
        public static string KindLabel(string kind)
        {
            if(kind=="phu-kien")return "Phụ kiện";
            if(kind=="ao-khac")return "Áo khác";
            var item=MenuSections("NU",new List<CategoryOption>()).SelectMany(g=>g.Items).FirstOrDefault(e=>e.Kind==kind);
            return item == null ? "" : item.Label;
        }
        public static bool IsAccessoryKind(string kind) { return new[]{"phu-kien","kinh-mat","mu","vo","day-nit","balo","tui-xach"}.Contains(kind); }
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
