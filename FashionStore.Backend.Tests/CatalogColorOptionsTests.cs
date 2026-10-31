using FashionStore.Backend.DTOs;
using FashionStore.Backend.Services;
using FashionStore.Web.Helpers;
using FashionStore.Web.Models;
using Xunit;

namespace FashionStore.Backend.Tests;

public sealed class CatalogColorOptionsTests
{
    [Fact]
    public void SizesCollapseIntoColorsAndKeepTheirOwnPhotoPriceAndStock()
    {
        var colors=CatalogColorOptions.Create([
            new("BLACK-S","S","Đen",200,2),new("BLACK-M","M","đen",150,3){OriginalPrice=200},
            new("RED-S","S","Đỏ",300,0),new("BLUE-S","S","Xanh dương",400,1)
        ],[new("BLACK-S","/black.png"),new("BLACK-M","/black.png"),new(null,"/red.png"){Color="Đỏ"}]);
        Assert.Equal(3,colors.Count);
        var black=colors[0];Assert.Equal(5,black.Stock);Assert.Equal(150,black.Price);Assert.Equal(200,black.OriginalPrice);Assert.Equal("/black.png",black.ImageUrl);
        Assert.Equal("/red.png",colors[1].ImageUrl);Assert.Equal(0,colors[1].Stock);
        Assert.Null(colors[2].ImageUrl); // Never reuse another color's image.
    }

    [Fact]
    public void SaleCardsKeepTheDiscountedSkuWhenOtherSizesHaveLowerRegularPrices()
    {
        VariantResponse[] variants=[new("R-S","S","Đỏ",50,2),new("R-M","M","Đỏ",80,2){OriginalPrice=100}];
        Assert.Equal(50,Assert.Single(CatalogColorOptions.Create(variants,[])).Price);
        var sale=Assert.Single(CatalogColorOptions.Create(variants,[],true));
        Assert.Equal(80,sale.Price);Assert.Equal(100,sale.OriginalPrice);
    }

    [Fact]
    public void DeepLinkKeepsTheRequestedColorEvenWhenItIsSoldOut()
    {
        ProductVariant[] variants=[new(){MaSKU="B",MauSac="Đen",SoLuongTonKho=2},new(){MaSKU="R",MauSac="Đỏ",SoLuongTonKho=0}];
        Assert.Equal("Đỏ",CatalogViewHelper.InitialColor(variants," đỏ "));
        Assert.Equal("Đen",CatalogViewHelper.InitialColor(variants,"unknown"));
    }

    [Fact]
    public void GalleryDeduplicatesSizesKeepsAnglesAndDoesNotBorrowOtherColorPhotos()
    {
        var model=new ProductDetailViewModel {
            Variants=[new(){MaSKU="B-S",MauSac="Đen"},new(){MaSKU="B-M",MauSac="Đen"},new(){MaSKU="R",MauSac="Đỏ"}],
            Images=[new(){MaSKU="B-S",DuongDan="/black.png"},new(){MaSKU="B-M",DuongDan="/black.png"},
                new(){MauSac="Đen",DuongDan="/black-detail.png"},new(){MaSKU="R",DuongDan="/red.png"},new(){MauSac="Đen",DuongDan="javascript:alert(1)"}]
        };
        Assert.Equal(new[]{"/black.png","/black-detail.png"},CatalogViewHelper.ColorPhotos(model,"Đen").Select(p=>p.DuongDan));
        Assert.Empty(CatalogViewHelper.ColorPhotos(model,"Xanh dương"));
        Assert.Null(CatalogViewHelper.PhotoPath(null));
    }
}
