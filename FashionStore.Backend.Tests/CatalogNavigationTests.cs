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
        Assert.Equal(audience!="NAM",items.Any(i=>i.Label=="Đầm"));
        Assert.DoesNotContain(items,i=>i.Label=="123");
        Assert.Contains(items,i=>i.Kind=="day-nit");
        Assert.True(CatalogViewHelper.IsAccessoryKind("day-nit"));
        Assert.False(CatalogViewHelper.IsAccessoryKind("ao-vest"));
        Assert.Contains(items, i => i.Kind == "quan-ngan"); Assert.Contains(items, i => i.Kind == "quan-dai");
        Assert.Contains(items, i => i.Kind == "giay"); Assert.Contains(items, i => i.Kind == "dep");
        Assert.Contains(items, i => i.Category == "OTHER");
        Assert.Equal("POLO", Assert.Single(items.Where(i => i.Label == "Áo polo")).Category);
        Assert.Equal("Quần dài", CatalogViewHelper.KindLabel("quan-dai"));
    }
}
