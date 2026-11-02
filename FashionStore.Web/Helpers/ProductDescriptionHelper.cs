using FashionStore.Web.Models;
using FashionStore.Catalog;

namespace FashionStore.Web.Helpers
{
    // Editorial fallbacks distinguish suggested fabrics from source product descriptions.
    public static class ProductDescriptionHelper
    {
        public sealed class Copy
        {
            public string Overview { get; set; }
            public string AdviceTitle { get; set; }
            public string Advice { get; set; }
            public string Care { get; set; }
            public ProductMaterialHelper.Analysis Material { get; set; }
        }

        public static Copy For(ProductSummary product)
        {
            var copy=BaseCopy(product);
            copy.Material=ProductMaterialHelper.For(product);
            if (System.String.IsNullOrWhiteSpace(product.MoTa) && copy.Material.Publish && !System.String.IsNullOrWhiteSpace(copy.Material.Care))
                copy.Care = copy.Material.Care;
            return copy;
        }

        private static Copy BaseCopy(ProductSummary product)
        {
            var rule=CatalogTaxonomy.Find(product.TenLoai);
            var kind=product.Kind??(rule==null?null:rule.Kind);
            switch(kind)
            {
                case "ao-thun": return Create("Áo thun là lựa chọn dễ kết hợp cho trang phục hằng ngày. Đổi màu áo hoặc cách phối để tạo diện mạo phù hợp với bạn.","Kết hợp cùng quần jeans, kaki hoặc quần short; thêm giày thể thao cho những buổi dạo phố.");
                case "ao-polo": return Create("Áo polo gợi phong cách gọn gàng, phù hợp khi bạn muốn cân bằng giữa trang phục thường ngày và vẻ chỉn chu.","Phối cùng quần dài cho ngày đi làm hoặc quần short cho những dịp cuối tuần.");
                case "ao-so-mi": return Create("Áo sơ mi mang đến nhiều cách phối, từ diện mạo chỉn chu đến trang phục dạo phố. Chọn màu áo phù hợp với tủ đồ của bạn.","Kết hợp với quần tây hoặc jeans; có thể khoác ngoài áo thun để tạo thêm lớp cho trang phục.");
                case "ao-khoac": case "ao-hoodie": case "ao-len":
                    return Create("Một lớp áo ngoài giúp tạo điểm nhấn và làm mới trang phục quen thuộc. Bạn có thể phối đồng màu hoặc chọn màu tương phản.","Mặc cùng áo thun bên trong, kết hợp quần jeans, jogger hoặc quần dài theo phong cách yêu thích.");
                case "ao-vest": return Create("Áo vest là gợi ý cho diện mạo chỉn chu. Chọn kích cỡ phù hợp với vòng vai và cách mặc của bạn.","Phối cùng sơ mi và quần tây cho dịp trang trọng; áo thun trơn là gợi ý cho cách mặc thường ngày.");
                case "quan-dai": return Create("Quần dài là lựa chọn linh hoạt để xây dựng nhiều bộ trang phục. Kết hợp màu quần với áo và phụ kiện để tạo tổng thể hài hòa.","Phối cùng áo thun cho ngày thường, polo hoặc sơ mi khi muốn diện mạo chỉn chu hơn.");
                case "quan-ngan": case "quan-jogger":
                    return Create("Gợi ý cho phong cách thường ngày và những buổi dạo phố. Chọn kích cỡ theo số đo thực tế và cách phối bạn yêu thích.","Kết hợp với áo thun, polo hoặc áo khoác; hoàn thiện trang phục bằng giày thể thao.");
                case "vay": case "vay-ngan": case "vay-dai": case "dam": case "dam-ngan": case "dam-dai":
                    return Create("Gợi ý cho phong cách nữ tính, từ những buổi gặp gỡ đến ngày dạo phố. Chọn màu và kích cỡ phù hợp với dịp sử dụng.","Kết hợp giày và túi xách đồng tông; thêm phụ kiện nhỏ để tạo điểm nhấn cho tổng thể.");
                case "do-the-thao": return Create("Gợi ý để xây dựng trang phục theo phong cách thể thao. Đối chiếu kích cỡ trước khi chọn để phù hợp với cách mặc của bạn.","Kết hợp cùng giày thể thao và phụ kiện gọn nhẹ, ưu tiên màu sắc hài hòa.");
                case "do-lot": return Create("Lựa chọn màu sắc và kích cỡ theo nhu cầu sử dụng hằng ngày. Hãy đối chiếu số đo thực tế trước khi chọn size.","Chọn kích cỡ vừa vặn; xem kỹ thông tin và hướng dẫn của sản phẩm trước khi sử dụng.","Gợi ý lựa chọn");
                case "giay": case "dep": return Create("Hoàn thiện trang phục với màu sắc và kiểu giày dép bạn yêu thích. Đối chiếu kích cỡ trước khi lựa chọn.","Phối với quần jeans, quần dài hoặc trang phục thường ngày; chọn màu đồng tông để dễ kết hợp.",care:"Làm sạch theo hướng dẫn của sản phẩm và chất liệu thực tế; để khô nơi thoáng mát.");
                case "kinh-mat": case "mu": case "vo": case "day-nit": case "balo": case "tui-xach": case "phu-kien":
                    return Create("Một chi tiết nhỏ để tạo dấu ấn riêng cho trang phục. Chọn màu phụ kiện đồng điệu hoặc tương phản với bộ đồ của bạn.","Kết hợp cùng trang phục đơn sắc để làm nổi bật phụ kiện, hoặc chọn cùng tông cho tổng thể hài hòa.",care:"Làm sạch theo hướng dẫn của sản phẩm; bảo quản nơi khô ráo khi không sử dụng.");
                default: return Create("Khám phá màu sắc và kích cỡ để lựa chọn sản phẩm phù hợp với phong cách của bạn.","Đối chiếu thông tin sản phẩm và nhu cầu sử dụng trước khi lựa chọn.","Gợi ý lựa chọn");
            }
        }

        private static Copy Create(string overview,string advice,string adviceTitle="Gợi ý phối đồ",string care=null)
        {
            return new Copy{Overview=overview,AdviceTitle=adviceTitle,Advice=advice,
                Care=care??"Đọc hướng dẫn trên nhãn; phân loại màu trước khi giặt và phơi nơi thoáng mát."};
        }
    }
}
