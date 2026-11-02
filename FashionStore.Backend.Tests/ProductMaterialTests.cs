using FashionStore.Web.Helpers;
using FashionStore.Web.Models;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class ProductMaterialTests
{
    [Theory]
    [InlineData("Boxer nam cotton","Cotton")]
    [InlineData("Áo lót nữ COTTON","Cotton")]
    [InlineData("Boxer nam modal","Modal")]
    [InlineData("Áo tập polyester","Polyester")]
    [InlineData("Áo tập polyeste","Polyester")]
    [InlineData("Áo linen","Linen")]
    [InlineData("Áo cotton polyester","Cotton / Polyester")]
    public void NamedFibersAreDistinguishedFromGuesses(string name,string expected)
    {
        var result=ProductMaterialHelper.For(new ProductSummary{TenSanPham=name});
        Assert.True(result.Publish);Assert.Equal("named-fiber",result.Status);Assert.Equal(expected,result.Label);
        Assert.DoesNotContain("100%",result.Description);
    }

    [Theory]
    [InlineData("Bộ thể thao nữ yoga","do-the-thao","Polyester")]
    [InlineData("Boxer nam thể thao","do-lot","Polyester")]
    [InlineData("Vớ thể thao","vo","Polyester")]
    [InlineData("Áo thun lạnh","ao-thun","Polyester")]
    [InlineData("Áo hoodie nỉ bông","ao-hoodie","Nỉ Cotton / Polyester")]
    [InlineData("Nón len","mu","Sợi dệt kim Acrylic pha")]
    [InlineData("Áo thun trơn nam","ao-thun","Cotton")]
    [InlineData("Áo thun nam tay dài","ao-thun","Cotton")]
    [InlineData("Áo sơ mi tay ngắn","ao-so-mi","Cotton")]
    [InlineData("Áo khoác blazer nữ","ao-khoac","Polyester / Viscose")]
    [InlineData("Quần tây nữ lưng cao","quan-dai","Polyester / Viscose")]
    [InlineData("Quần tây nam co giãn","quan-dai","Vải pha Elastane")]
    [InlineData("Áo lót nữ không đường may","do-lot","Nylon / Elastane")]
    [InlineData("Váy nữ xếp ly","vay","Polyester")]
    [InlineData("Vớ cổ trung","vo","Cotton pha sợi đàn hồi")]
    [InlineData("Nón lưỡi trai","mu","Cotton dệt thoi")]
    [InlineData("Áo khoác trench coat nữ","ao-khoac","Cotton / Polyester dệt thoi")]
    [InlineData("Áo khoác lông nữ","ao-khoac","Lông tổng hợp Polyester")]
    [InlineData("Nón beret","mu","Vải dạ pha")]
    public void InferredMaterialsRemainClearlyLabelledSuggestions(string name,string kind,string expected)
    {
        var result=ProductMaterialHelper.For(new ProductSummary{TenSanPham=name,Kind=kind});
        Assert.True(result.Publish);Assert.True(result.IsSuggested);Assert.Equal("suggested",result.Status);
        Assert.Equal(expected,result.Label);Assert.Equal("Chất liệu tham khảo",result.Heading);
        Assert.Null(result.Note);
        Assert.Equal(2,result.Features.Count);Assert.False(string.IsNullOrWhiteSpace(result.Care));
        Assert.DoesNotContain("100%",result.Description);
    }

    [Theory]
    [InlineData("Test","ao-thun")]
    [InlineData("Thử nghiệm áo","ao-khoac")]
    [InlineData("Áo không cotton","ao-thun")]
    [InlineData("Áo không polyester","do-the-thao")]
    [InlineData("","ao-thun")]
    [InlineData("Mẫu mới chưa phân loại",null)]
    public void PlaceholdersAndExclusionsDoNotAcquireGuessedComposition(string name,string? kind)
    {
        var result=ProductMaterialHelper.For(new ProductSummary{TenSanPham=name,Kind=kind});
        Assert.False(result.Publish);Assert.Null(result.Label);Assert.Empty(result.Features);
    }

    [Fact]
    public void NameEvidenceWinsOverBroadCategoryAndCareDoesNotOverrideDatabaseCopy()
    {
        var product=new ProductSummary{TenSanPham="Boxer thể thao cotton",Kind="do-the-thao"};
        Assert.Equal("named-fiber",ProductMaterialHelper.For(product).Status);
        Assert.Equal("Cotton",ProductMaterialHelper.For(product).Label);
        var inferred=new ProductSummary{TenSanPham="Áo hoodie nam",Kind="ao-hoodie"};
        Assert.Equal(ProductMaterialHelper.For(inferred).Care,ProductDescriptionHelper.For(inferred).Care);
        inferred.MoTa="Mô tả đã được nhà cung cấp xác nhận.";
        Assert.NotEqual(ProductMaterialHelper.For(inferred).Care,ProductDescriptionHelper.For(inferred).Care);
    }

    [Fact]
    public void CategoryFallbackSupportsItemsWithoutKind()
    {
        var result=ProductMaterialHelper.For(new ProductSummary{TenSanPham="Mẫu cổ tròn",TenLoai="Áo thun nam"});
        Assert.True(result.IsSuggested);Assert.Equal("Cotton",result.Label);
    }

    [Theory]
    [InlineData("Quần jeans nữ ống rộng")]
    [InlineData("Áo khoác denim nam")]
    public void DenimIsAFabricTypeAndDoesNotPromiseCottonContent(string name)
    {
        var result=ProductMaterialHelper.For(new ProductSummary{TenSanPham=name});
        Assert.True(result.Publish);Assert.Equal("named-fabric",result.Status);Assert.Equal("Denim",result.Label);
        Assert.DoesNotContain("Cotton",result.Label);
    }

    [Fact]
    public void DesignCopyUsesOnlyNamedFeaturesAndStaysCompact()
    {
        var result=ProductMaterialHelper.For(new ProductSummary{TenSanPham="Quần nữ ống rộng lưng cao xếp ly"});
        Assert.Equal(2,result.DesignNotes.Count);
        Assert.Contains(result.DesignNotes,n=>n.Contains("ống rộng"));
        Assert.Contains(result.DesignNotes,n=>n.Contains("lưng cao"));
        Assert.Empty(ProductMaterialHelper.For(new ProductSummary{TenSanPham="Test"}).DesignNotes);
    }
}
