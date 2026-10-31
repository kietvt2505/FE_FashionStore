#if NET8_0_OR_GREATER
// Keep this shared source compatible with the MVC project's older C# compiler.
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FashionStore.Catalog
{
    // Shared by MVC navigation and the read-only Supabase catalog query.
    public static class CatalogTaxonomy
    {
        public sealed class Rule
        {
            public string Name { get; set; }
            public string Audience { get; set; }
            public string Kind { get; set; }
            public string Section { get; set; }
        }

        public static readonly string[] AccessoryKinds = { "phu-kien", "kinh-mat", "mu", "vo", "day-nit", "balo", "tui-xach" };
        public static readonly IReadOnlyList<Rule> Rules = CreateRules();

        public static string Normalize(string value)
        {
            var plain = new string((value ?? "").Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
            return Regex.Replace(plain.ToLowerInvariant().Replace('đ', 'd').Replace('-', ' ').Trim(), @"\s+", " ");
        }

        private static IReadOnlyList<Rule> CreateRules()
        {
            var result = new List<Rule>();
            Action<string,string,string,string> add = (names, kind, section, neutralAudience) => {
                foreach (var name in names.Split('|')) {
                    result.Add(new Rule { Name = Normalize(name), Kind = kind, Section = section, Audience = neutralAudience });
                    result.Add(new Rule { Name = Normalize(name + " nam"), Kind = kind, Section = section, Audience = "NAM" });
                    result.Add(new Rule { Name = Normalize(name + " nữ"), Kind = kind, Section = section, Audience = "NU" });
                }
            };
            add("Áo thun|Áo phông|Baby tee|Áo thun baby tee", "ao-thun", "shirts", null);
            add("Áo polo", "ao-polo", "shirts", null);
            add("Áo sơ mi", "ao-so-mi", "shirts", null);
            add("Áo khoác", "ao-khoac", "shirts", null);
            add("Áo hoodie", "ao-hoodie", "shirts", null);
            add("Áo vest", "ao-vest", "shirts", null);
            add("Áo len", "ao-len", "shirts", null);
            // Broad categories do not override a product's more specific manual classification.
            add("Áo", null, "shirts", null);
            add("Quần", null, "bottoms", null);
            add("Quần dài|Quần tây|Quần âu|Quần jeans|Quần jean|Quần bò|Quần kaki", "quan-dai", "bottoms", null);
            add("Quần ngắn|Quần short|Quần shorts", "quan-ngan", "bottoms", null);
            add("Quần jogger", "quan-jogger", "bottoms", null);
            add("Váy", "vay", "bottoms", "NU"); add("Váy ngắn", "vay-ngan", "bottoms", "NU"); add("Váy dài", "vay-dai", "bottoms", "NU");
            add("Đầm", "dam", "bottoms", "NU"); add("Đầm ngắn", "dam-ngan", "bottoms", "NU"); add("Đầm dài", "dam-dai", "bottoms", "NU");
            add("Đồ thể thao|Bộ thể thao", "do-the-thao", "clothing", null);
            add("Đồ lót|Áo lót|Quần lót", "do-lot", "clothing", null);
            add("Giày", "giay", "footwear", "UNISEX"); add("Dép|Sandal|Sandals", "dep", "footwear", "UNISEX");
            add("Phụ kiện", "phu-kien", "accessories", "UNISEX");
            add("Kính mát|Mắt kính mát|Kính râm|Mắt kính râm", "kinh-mat", "accessories", "UNISEX");
            add("Mũ|Nón", "mu", "accessories", "UNISEX"); add("Vớ|Tất", "vo", "accessories", "UNISEX");
            add("Dây nịt|Dây nịch", "day-nit", "accessories", "UNISEX"); add("Balo|Ba lô", "balo", "accessories", "UNISEX");
            add("Túi xách|Túi sách", "tui-xach", "accessories", "NU");
            return result.GroupBy(r => r.Name).Select(g => g.First()).ToList();
        }

        public static Rule Find(string category) { var name = Normalize(category); return Rules.FirstOrDefault(r => r.Name == name); }
        public static string Audience(string category)
        {
            var rule = Find(category); if (rule != null && rule.Audience != null) return rule.Audience;
            var name = Normalize(category); var male = Regex.IsMatch(name, @"\bnam\b"); var female = Regex.IsMatch(name, @"\bnu\b");
            if (male != female) return male ? "NAM" : "NU";
            if (Regex.IsMatch(name, @"\b(vay|dam)\b")) return "NU";
            return null;
        }
        public static bool MatchesAudience(string category, string audience)
        {
            var actual = Audience(category);
            return String.IsNullOrEmpty(audience) || actual == null || actual == audience || actual == "UNISEX" && (audience == "NAM" || audience == "NU");
        }
        public static string Section(string category)
        {
            var rule = Find(category); if (rule != null) return rule.Section;
            var name = Normalize(category);
            if (Regex.IsMatch(name, @"\b(quan|vay|dam)\b")) return "bottoms";
            if (Regex.IsMatch(name, @"\b(giay|dep)\b")) return "footwear";
            if (Regex.IsMatch(name, @"\b(the thao|do lot)\b")) return "clothing";
            if (Regex.IsMatch(name, @"\bao\b")) return "shirts";
            return "other";
        }
        public static string KindLabel(string kind)
        {
            var labels = new Dictionary<string,string> {
                {"ao-thun","Áo thun"},{"ao-polo","Áo polo"},{"ao-so-mi","Áo sơ mi"},{"ao-khoac","Áo khoác"},{"ao-hoodie","Áo hoodie"},{"ao-vest","Áo vest"},{"ao-len","Áo len"},{"ao-khac","Áo khác"},
                {"quan-ngan","Quần ngắn"},{"quan-dai","Quần dài"},{"quan-jogger","Quần jogger"},{"vay","Váy"},{"vay-ngan","Váy ngắn"},{"vay-dai","Váy dài"},{"dam","Đầm"},{"dam-ngan","Đầm ngắn"},{"dam-dai","Đầm dài"},
                {"do-the-thao","Đồ thể thao"},{"do-lot","Đồ lót"},{"giay","Giày"},{"dep","Dép"},{"phu-kien","Phụ kiện"},{"kinh-mat","Kính mát"},{"mu","Mũ / Nón"},{"vo","Vớ"},{"day-nit","Dây nịt"},{"balo","Balo"},{"tui-xach","Túi xách"}
            };
            return kind != null && labels.ContainsKey(kind) ? labels[kind] : "";
        }
    }
}
