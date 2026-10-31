using FashionStore.Web.Helpers;
using FashionStore.Web.Models;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class CatalogNavigationTests
{
    [Theory]
    [InlineData("NAM","Nữ","Nam")]
    [InlineData("NU","Nam","Nữ")]
    public void GenderedDatabaseCategoriesDoNotLeakIntoOppositeMenu(string audience,string excluded,string included)
    {
        var labels=new[]{"Áo hoodie nam","Áo khoác nam","Áo khoác nữ","Áo thun baby tee nữ","Áo thun nam","Áo vest nam",
            "Đồ thể thao nam","Đồ thể thao nữ","Quần jeans nam","Quần jeans nữ","Quần tây nam","Quần tây nữ","Đồ lót nam","Đồ lót nữ"};
        var categories=labels.Select((label,index)=>new CategoryOption{MaLoai="CAT"+index,TenLoai=label}).ToList();
        categories.Add(new(){MaLoai="SHARED",TenLoai="Áo len"});
        categories.Add(new(){MaLoai="BOTH",TenLoai="Áo thể thao nam/nữ"});
        categories.Add(new(){MaLoai="LOWER",TenLoai="  QUẦN THỂ THAO NỮ  "});
        categories.Add(new(){MaLoai="SKIRT",TenLoai="Đầm công sở"});
        var items=CatalogViewHelper.MenuSections(audience,categories).SelectMany(s=>s.Items).ToList();
        foreach(var label in labels){
            if(label.EndsWith(excluded,StringComparison.OrdinalIgnoreCase))Assert.DoesNotContain(items,i=>i.Label==label);
            if(label.EndsWith(included,StringComparison.OrdinalIgnoreCase))Assert.Contains(items,i=>i.Label==label);
        }
        Assert.Contains(items,i=>i.Category=="SHARED");Assert.Contains(items,i=>i.Category=="BOTH");
        Assert.Equal(audience=="NU",items.Any(i=>i.Category=="LOWER"));
        Assert.Equal(audience=="NU",items.Any(i=>i.Category=="SKIRT"));
    }
    [Theory]
    [InlineData("NAM", false)]
    [InlineData("NU", true)]
    [InlineData("TRE_EM", false)]
    public void MenuRetainsExistingCategoriesAndRestrictsFemaleOnlyItems(string audience, bool female)
    {
        var categories = new List<CategoryOption> {
            new() { MaLoai="AO",TenLoai="Áo" }, new() { MaLoai="POLO",TenLoai="Áo polo" },
            new() { MaLoai="TUI",TenLoai="Túi xách" }, new() { MaLoai="VAY",TenLoai="Váy ngắn" },
            new() { MaLoai="OTHER",TenLoai="Áo len" },new() {MaLoai="DRESS",TenLoai="Đầm"},new(){MaLoai="TEST",TenLoai="123"}
        };
        var items = CatalogViewHelper.MenuSections(audience, categories).SelectMany(s => s.Items).ToList();
        Assert.Equal(female, items.Any(i => i.Label == "Túi xách"));
        Assert.Equal(female, items.Any(i => i.Label == "Váy ngắn"));
        Assert.Equal(audience=="NU",items.Any(i=>i.Label=="Đầm"));
        Assert.DoesNotContain(items,i=>i.Label=="123");
        Assert.True(CatalogViewHelper.IsAccessoryKind("day-nit"));
        Assert.False(CatalogViewHelper.IsAccessoryKind("ao-vest"));
        Assert.DoesNotContain(items,i=>i.Label=="Quần ngắn"||i.Label=="Giày"||i.Label=="Dép"); // No links to nonexistent categories.
        Assert.All(items,i=>{Assert.False(String.IsNullOrEmpty(i.Category));Assert.Null(i.Kind);});
        Assert.Contains(items, i => i.Category == "OTHER");
        Assert.Equal("POLO", Assert.Single(items,i => i.Label == "Áo polo").Category);
        Assert.Equal("Quần dài", CatalogViewHelper.KindLabel("quan-dai"));
    }

    [Fact]
    public void CurrentCategoriesUseOneExactFilterAndAccessoriesExcludeClothing()
    {
        var categories=new List<CategoryOption>{
            new(){MaLoai="T-M",TenLoai="Áo thun nam"},new(){MaLoai="T-F",TenLoai="Áo thun baby tee nữ"},
            new(){MaLoai="P-M",TenLoai="Quần tây nam"},new(){MaLoai="P-F",TenLoai="Quần jeans nữ"},
            new(){MaLoai="U-M",TenLoai="Đồ lót nam"},new(){MaLoai="D-F",TenLoai="Đầm"},
            new(){MaLoai="H",TenLoai="Nón"},new(){MaLoai="S",TenLoai="Vớ"}
        };
        var male=CatalogViewHelper.MenuSections("NAM",categories).SelectMany(s=>s.Items).ToList();
        Assert.Equal(new[]{"T-M","P-M","U-M","H","S"}.OrderBy(x=>x),male.Select(i=>i.Category).OrderBy(x=>x));
        Assert.All(male,i=>Assert.Null(i.Kind));
        Assert.Equal(new[]{"H","S"},CatalogViewHelper.CategoriesForContext(categories,null,"phu-kien").Select(c=>c.MaLoai));
        Assert.Equal("Trang phục khác",CatalogViewHelper.MenuSections("NAM",categories).Single(s=>s.Items.Any(i=>i.Category=="U-M")).Title);
        Assert.Equal(new[]{"P-M"},CatalogViewHelper.CategoriesForContext(categories,"NAM","quan-dai").Select(c=>c.MaLoai));
    }
}
