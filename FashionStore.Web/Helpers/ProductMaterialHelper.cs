using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FashionStore.Catalog;
using FashionStore.Web.Models;

namespace FashionStore.Web.Helpers
{
    public static class ProductMaterialHelper
    {
        public sealed class Analysis
        {
            public string Status { get; set; }
            public string Label { get; set; }
            public string Heading { get; set; }
            public string Description { get; set; }
            public string Note { get; set; }
            public string ReviewGroup { get; set; }
            public string Evidence { get; set; }
            public string Care { get; set; }
            public bool IsSuggested { get { return Status == "suggested"; } }
            public bool Publish { get { return Status == "named-fiber" || Status == "named-fabric" || IsSuggested; } }
            public IList<string> Features { get; set; } = new List<string>();
            public IList<string> DesignNotes { get; set; } = new List<string>();
        }

        public static Analysis For(ProductSummary product)
        {
            var name=CatalogTaxonomy.Normalize(product.TenSanPham);
            var rule = CatalogTaxonomy.Find(product.TenLoai);
            var kind = product.Kind ?? (rule == null ? null : rule.Kind);
            var result=AnalyzeName(name,kind);
            result.DesignNotes=DesignNotes(name);
            return result;
        }

        private static Analysis AnalyzeName(string name,string kind)
        {
            var cotton=Regex.IsMatch(name,@"\bcotton\b")&&!Regex.IsMatch(name,@"\bkhong cotton\b");
            var polyester=Regex.IsMatch(name,@"\b(polyester|polyeste)\b")&&!Regex.IsMatch(name,@"\bkhong (polyester|polyeste)\b");
            if(cotton&&polyester)return Named("Cotton / Polyester","Tên sản phẩm ghi cả Cotton và Polyester.",
                "Phối sợi cotton và polyester. Đặc điểm của vải phụ thuộc tỉ lệ pha, cấu trúc dệt và cách hoàn tất sản phẩm.");
            if(cotton)return Named("Cotton","Tên sản phẩm ghi Cotton.",
                "Cotton là sợi bông tự nhiên, có khả năng hút ẩm. Cảm giác khi mặc còn phụ thuộc tỉ lệ sợi và cấu trúc vải.");
            if(polyester)return Named("Polyester","Tên sản phẩm ghi Polyester hoặc Polyeste.",
                "Polyester là sợi tổng hợp dùng trong may mặc, có thể được phối cùng các loại sợi khác. Không mặc định các tính năng thể thao cho mọi loại vải.");
            if(Regex.IsMatch(name,@"\bmodal\b"))return Named("Modal","Tên sản phẩm ghi Modal.",
                "Modal là sợi cellulose tái sinh, được biết đến với cảm giác mềm mại. Đặc điểm của sản phẩm còn phụ thuộc thành phần pha và cấu trúc vải.");
            if(Regex.IsMatch(name,@"\blinen\b"))
                return Named("Linen","Tên sản phẩm ghi Linen.","Tên sản phẩm đề cập đến sợi lanh. Đối chiếu thành phần trên nhãn để biết sản phẩm dùng linen nguyên chất hay vải pha.");
            if(Regex.IsMatch(name,@"\b(jeans|denim)\b"))return new Analysis{
                Status="named-fabric",Heading="Kiểu vải",Label="Denim",ReviewGroup="Denim — chưa xác nhận thành phần sợi",
                Evidence="Tên sản phẩm ghi jeans hoặc denim; đây là kiểu vải, không phải tỉ lệ sợi cotton.",
                Description="Denim là kiểu vải dệt chéo thường dùng cho trang phục jeans, tạo điểm nhấn cho phong cách thường ngày.",
                Note="Thành phần sợi và độ co giãn: xem nhãn của từng sản phẩm.",
                Features=new List<string>{"Bề mặt dệt chéo tạo nét đặc trưng cho trang phục jeans.","Màu wash, độ dày và độ co giãn có thể khác nhau giữa các mẫu."},
                Care="Lộn trái, phân loại màu và giặt theo nhãn; hạn chế chà mạnh ở vùng wash hoặc rách trang trí."};
            // Explicit exclusions and placeholder names must never acquire a guessed fiber.
            if (String.IsNullOrWhiteSpace(name) || Regex.IsMatch(name,@"\b(test|thu nghiem|khong cotton|khong polyester|khong polyeste)\b"))
                return Review("Chưa đủ dữ liệu", "Tên thử nghiệm hoặc loại trừ chất liệu; cần thông tin riêng của sản phẩm.");
            if(Regex.IsMatch(name,@"\b(the thao|thun lanh|chay bo|yoga|gym|training|tennis|dap xe)\b")||kind=="do-the-thao")
                return Suggest("Polyester", "Tên hoặc loại sản phẩm thể thao/thun lạnh.",
                    "Polyester là sợi tổng hợp thường dùng trong trang phục thể thao. Cấu trúc dệt kim hoặc dệt lưới mang đến nhiều lựa chọn về bề mặt và kiểu dáng.",
                    "Phù hợp tham khảo khi chọn trang phục cho phong cách vận động, từ tập luyện đến dạo phố.",
                    "Chọn độ dày, độ thoáng và độ co giãn theo cường độ vận động; đối chiếu thông số của mẫu trước khi mua.",
                    "Giặt theo nhãn, tránh nguồn nhiệt cao; không tự dùng chất tẩy hoặc nước xả khi nhãn không cho phép.");
            if (Regex.IsMatch(name,@"\bkhong duong may\b"))
                return Suggest("Nylon / Elastane", "Tên ghi thiết kế không đường may.",
                    "Gợi ý nền vải dệt kim Nylon phối Elastane cho thiết kế không đường may. Elastane thường được dùng để bổ sung độ đàn hồi; mức ôm thực tế còn phụ thuộc cấu trúc và tỉ lệ pha.",
                    "Gợi ý bề mặt liền mạch, ít chi tiết nổi để phối bên trong trang phục.",
                    "Chọn độ ôm theo số đo cơ thể, không chỉ dựa vào tên chất liệu.",
                    "Theo nhãn chăm sóc; nếu được phép giặt máy, dùng túi giặt và chế độ nhẹ, tránh kéo vặn mạnh.");
            if (Regex.IsMatch(name,@"\b(co gian|om body)\b"))
                return Suggest("Vải pha Elastane", "Tên đề cập đến co giãn hoặc dáng ôm body.",
                    "Gợi ý nền vải pha Elastane để bổ sung độ đàn hồi cho kiểu dáng ôm hoặc co giãn. Tên sản phẩm chưa đủ để xác định sợi nền là Cotton, Polyester hay sợi khác.",
                    "Hướng chất liệu ưu tiên độ linh hoạt khi thay đổi tư thế.",
                    "Độ giãn và khả năng hồi phục cần được đối chiếu trên sản phẩm thực tế.",
                    "Ưu tiên hướng dẫn trên nhãn; tránh kéo giãn khi vải còn ướt và tránh sấy nhiệt cao nếu không được phép.");
            if (Regex.IsMatch(name,@"\b(hoodie|ni bong)\b") || kind=="ao-hoodie")
                return Suggest("Nỉ Cotton / Polyester", "Tên hoodie hoặc nỉ bông.",
                    "Gợi ý vải nỉ pha Cotton và Polyester cho lớp áo mặc ngoài. Cotton hướng đến cảm giác tiếp xúc và hút ẩm, còn cấu trúc nỉ tạo bề mặt riêng cho phong cách thường ngày.",
                    "Có thể chọn mặt nỉ vòng hoặc nỉ chải tùy độ dày mong muốn; chưa xác định loại mặt trong của mẫu này.",
                    "Độ dày và mức giữ ấm phụ thuộc cấu trúc vải, không suy ra chỉ từ tên hoodie.",
                    "Lộn trái, phân loại màu và giặt theo nhãn; hạn chế ma sát mạnh ở bề mặt nỉ hoặc họa tiết.");
            if (Regex.IsMatch(name,@"\b(long|gia long)\b"))
                return Suggest("Lông tổng hợp Polyester", "Tên áo khoác lông.",
                    "Gợi ý bề mặt lông tổng hợp từ Polyester cho lớp áo khoác tạo điểm nhấn. Đây là phương án biên tập theo tên, chưa xác nhận loại sợi hoặc cấu tạo lớp lót.",
                    "Bề mặt dạng lông tạo chiều sâu cho trang phục và cần giữ nếp sợi khi bảo quản.",
                    "Độ dày, trọng lượng và mức giữ ấm cần kiểm tra trên từng mẫu áo.",
                    "Ưu tiên tuyệt đối nhãn chăm sóc; không tự giặt máy hoặc sấy nóng khi chưa biết cách xử lý bề mặt lông.");
            if (Regex.IsMatch(name,@"\b(cardigan|len)\b") || kind=="ao-len")
                return Suggest("Sợi dệt kim Acrylic pha", "Tên cardigan hoặc đồ len chỉ gợi kiểu dệt.",
                    "Gợi ý sợi Acrylic pha cho bề mặt dệt kim. Tên gọi len hoặc cardigan không đồng nghĩa sản phẩm có sợi len động vật; thành phần cần xem trên nhãn.",
                    "Cấu trúc đan tạo đường nét riêng, phù hợp cách phối nhiều lớp.",
                    "Độ mềm, độ dày và độ đàn hồi phụ thuộc sợi pha và mật độ mũi đan.",
                    "Giặt nhẹ theo nhãn, không kéo vặn; nếu nhãn cho phép, phơi phẳng để hạn chế kéo giãn dáng đan.");
            if (Regex.IsMatch(name,@"\bberet\b"))
                return Suggest("Vải dạ pha", "Tên mũ beret.",
                    "Gợi ý vải dạ pha cho phom mũ beret. Dạ mô tả kiểu bề mặt vải; chưa xác định sản phẩm dùng len, Polyester hay loại sợi pha nào.",
                    "Hướng bề mặt vải gọn, hỗ trợ tạo nếp và dáng mũ.",
                    "Có thể phối cùng áo khoác hoặc trang phục đơn sắc để nhấn vào phụ kiện.",
                    "Vệ sinh theo nhãn; dùng dụng cụ mềm phù hợp bề mặt và cất nơi khô ráo, tránh đè gập dáng mũ.");
            if (Regex.IsMatch(name,@"\b(quan tay|quan au|ao vest|blazer|tuxedo)\b") || kind=="ao-vest")
                return Suggest("Polyester / Viscose", "Tên quần tây, vest hoặc blazer.",
                    "Gợi ý vải dệt thoi pha Polyester và Viscose cho nhóm trang phục chỉn chu. Viscose là sợi cellulose tái sinh; tỉ lệ pha và cấu trúc vải quyết định cảm giác mặc và độ rủ.",
                    "Hướng bề mặt gọn, phù hợp các đường ly, đường may và phom dáng công sở.",
                    "Độ đứng phom hoặc độ rủ cần đánh giá cùng lớp lót và kỹ thuật may của từng mẫu.",
                    "Theo nhãn chăm sóc; với áo có lớp dựng hoặc lót, không tự ngâm giặt nếu nhãn yêu cầu chăm sóc chuyên nghiệp.");
            if (Regex.IsMatch(name,@"\b(trench coat|tui hop|kaki)\b"))
                return Suggest("Cotton / Polyester dệt thoi", "Tên trench coat, túi hộp hoặc kaki.",
                    "Gợi ý vải dệt thoi Cotton phối Polyester cho thiết kế có đường nét rõ. Đây là hướng chất liệu để biên tập mô tả, chưa xác nhận độ dày hay xử lý bề mặt.",
                    "Gợi ý cấu trúc vải phù hợp chi tiết túi, cổ và đường may tạo phom.",
                    "Có thể phối cùng áo thun hoặc sơ mi để tạo trang phục nhiều lớp.",
                    "Giặt theo nhãn, đóng khóa hoặc cài cúc trước khi giặt; làm phẳng và phơi đúng dáng áo.");
            if (Regex.IsMatch(name,@"\b(khoac gio|khoac du|bomber)\b") || kind=="ao-khoac")
                return Suggest("Polyester dệt thoi", "Tên áo khoác gió, dù, bomber hoặc nhóm áo khoác.",
                    "Gợi ý lớp vải ngoài Polyester dệt thoi cho áo khoác. Cấu trúc vải, lớp lót và cách hoàn tất sẽ tạo khác biệt về cảm giác mặc giữa các mẫu.",
                    "Hướng chất liệu phù hợp phối lớp với áo thun hoặc sơ mi.",
                    "Lựa chọn lớp lót và độ dày phù hợp thời tiết; xem thông số riêng nếu cần tính chống nước hoặc chống nắng.",
                    "Theo nhãn, đóng khóa trước khi giặt; không dùng nhiệt cao hoặc hóa chất mạnh khi chưa rõ lớp hoàn tất bề mặt.");
            if (Regex.IsMatch(name,@"\b(dam|vay)\b"))
                return Suggest("Polyester", "Tên váy hoặc đầm không ghi thành phần.",
                    "Polyester có thể được dệt thành nhiều bề mặt cho váy và đầm. Cấu trúc dệt thoi hoặc dệt kim tạo nên các lựa chọn khác nhau về độ rủ và độ đứng dáng.",
                    "Hướng chất liệu dễ triển khai nhiều kiểu dáng như suông, xếp ly hoặc maxi.",
                    "Chọn độ dày, lớp lót và độ rủ theo dịp mặc; không suy ra độ co giãn chỉ từ tên vải.",
                    "Theo nhãn, tránh kéo mạnh ở nếp ly và chi tiết trang trí; treo hoặc phơi theo cấu trúc của từng mẫu.");
            if (Regex.IsMatch(name,@"\b(do ngu|thoang khi|brief|lot|boxer)\b") || kind=="do-lot")
                return Suggest("Cotton", "Tên đồ mặc trong hoặc đồ ngủ không ghi thành phần.",
                    "Cotton là sợi bông tự nhiên có khả năng hút ẩm, thường dùng cho trang phục mặc hằng ngày. Cấu trúc dệt và cách hoàn tất ảnh hưởng đến bề mặt và cảm giác tiếp xúc.",
                    "Ưu tiên lựa chọn kích cỡ vừa vặn và bề mặt phù hợp nhu cầu mặc trong.",
                    "Đường may, phần cạp và độ ôm là những chi tiết nên đối chiếu khi chọn trang phục mặc trong.",
                    "Theo nhãn chăm sóc, giặt riêng màu phù hợp và để khô hoàn toàn trước khi cất; tránh chất tẩy mạnh nếu không được phép.");
            if (Regex.IsMatch(name,@"\b(vo|tat)\b") || kind=="vo")
                return Suggest("Cotton pha sợi đàn hồi", "Tên hoặc loại vớ/tất thường ngày.",
                    "Gợi ý nền Cotton phối sợi đàn hồi cho vớ thường ngày. Cotton đóng góp đặc tính hút ẩm, còn phần sợi đàn hồi là hướng lựa chọn để giữ độ ôm của cổ vớ.",
                    "Độ dày đệm, độ ôm và chiều cao cổ vớ cần chọn theo đôi giày và nhu cầu sử dụng.",
                    "Chưa xác nhận loại sợi đàn hồi hoặc tỉ lệ Cotton của sản phẩm.",
                    "Giặt theo nhãn, dùng túi giặt nếu phù hợp; không kéo mạnh phần cổ và tránh nhiệt cao khi nhãn không cho phép.");
            if (Regex.IsMatch(name,@"\b(non|mu)\b") || kind=="mu")
                return Suggest("Cotton dệt thoi", "Tên mũ lưỡi trai, bucket hoặc vành rộng.",
                    "Gợi ý vải Cotton dệt thoi cho mũ thường ngày. Kiểu dệt và lớp dựng góp phần tạo phom; tên sản phẩm chưa cho biết độ cứng của vành hoặc độ dày của vải.",
                    "Hướng bề mặt vải phù hợp cách phối thường ngày cùng áo thun, sơ mi hoặc áo khoác.",
                    "Chọn độ sâu lòng mũ, kích thước và kiểu vành phù hợp; xem thông số riêng nếu cần bảo vệ UV.",
                    "Đối chiếu nhãn trước khi giặt; làm sạch nhẹ và giữ dáng vành, không tự ngâm phần dựng mũ.");
            if (Regex.IsMatch(name,@"\b(thun|baby tee|polo|so mi|jogger|short)\b") || kind=="ao-thun" || kind=="ao-polo" || kind=="ao-so-mi" || kind=="quan-ngan" || kind=="quan-jogger") {
                var structure = Regex.IsMatch(name,@"\bso mi\b") || kind=="ao-so-mi"
                    ? "Gợi ý nền dệt thoi cho phần cổ, nẹp cúc và các đường may rõ nét."
                    : Regex.IsMatch(name,@"\b(short|jogger)\b") || kind=="quan-ngan" || kind=="quan-jogger"
                        ? "Chọn cấu trúc vải theo độ đứng phom và độ linh hoạt mong muốn khi ngồi hoặc di chuyển."
                        : Regex.IsMatch(name,@"\bpolo\b") || kind=="ao-polo"
                            ? "Gợi ý nền dệt kim, có thể chọn kiểu dệt pique để tạo điểm nhấn bề mặt cho áo có cổ."
                            : "Gợi ý nền dệt kim cho bề mặt áo thun; chọn độ dày và độ ôm phù hợp cách mặc hằng ngày.";
                return Suggest("Cotton", "Tên hoặc loại trang phục Cotton thường dùng.",
                    "Cotton là sợi bông tự nhiên có khả năng hút ẩm, thường dùng cho trang phục hằng ngày. Cấu trúc dệt và cách hoàn tất tạo nên những bề mặt, độ dày và phom dáng khác nhau.",
                    structure,
                    "Dễ kết hợp cùng trang phục đơn sắc hoặc phụ kiện để tạo diện mạo phù hợp từng dịp.",
                    "Lộn trái và phân loại màu trước khi giặt; chọn chế độ giặt, phơi và là theo nhãn sản phẩm.");
            }
            return Review("Chưa đủ dữ liệu", "Tên và kiểu dáng chưa cho biết thành phần sợi; cần nhãn hoặc thông tin nhà cung cấp.");
        }

        private static Analysis Named(string label,string evidence,string description)
        {
            var profile = NamedProfile(label);
            return new Analysis{Status="named-fiber",Heading="Chất liệu",Label=label,ReviewGroup=label,
                Evidence=evidence,Description=description,Note="Tỉ lệ thành phần và hướng dẫn sử dụng: xem nhãn sản phẩm.",
                Features=profile.Features,Care=profile.Care};
        }

        private static Analysis NamedProfile(string label)
        {
            if (label == "Modal") return new Analysis {
                Features = new List<string> {"Sợi cellulose tái sinh là hướng chất liệu cho bề mặt mềm mại.","Độ rủ và độ đàn hồi phụ thuộc cấu trúc dệt cùng các sợi phối."},
                Care = "Theo nhãn chăm sóc, giặt nhẹ và tránh kéo vặn khi ướt; phơi nơi thoáng mát."};
            if (label == "Linen") return new Analysis {
                Features = new List<string> {"Sợi lanh tạo bề mặt đặc trưng cho trang phục thường ngày.","Nếp nhăn và cảm giác bề mặt có thể thay đổi theo tỉ lệ pha và cách hoàn tất."},
                Care = "Giặt, phơi và là theo nhãn; làm phẳng nhẹ trước khi phơi, không dùng nhiệt vượt mức cho phép."};
            if (label == "Polyester") return new Analysis {
                Features = new List<string> {"Sợi tổng hợp có thể tạo nhiều bề mặt dệt kim hoặc dệt thoi.","Độ thoáng, độ co giãn và tính nhanh khô cần xem thông số riêng của mẫu."},
                Care = "Theo nhãn chăm sóc, phân loại màu; tránh nguồn nhiệt cao và chất tẩy không được cho phép."};
            return new Analysis {
                Features = new List<string> {"Cotton đóng góp đặc tính hút ẩm cho nền vải.","Độ mềm, độ dày và độ đàn hồi thay đổi theo sợi phối và cấu trúc dệt."},
                Care = "Lộn trái, phân loại màu và giặt theo nhãn; phơi nơi thoáng mát, điều chỉnh nhiệt độ là theo thành phần thực tế."};
        }

        private static Analysis Suggest(string label, string evidence, string description, string firstFeature, string secondFeature, string care)
        {
            return new Analysis {
                Status = "suggested", Heading = "Chất liệu tham khảo", Label = label,
                ReviewGroup = label + " (gợi ý)", Evidence = evidence, Description = description,
                Features = new List<string> {firstFeature, secondFeature}, Care = care
            };
        }

        private static Analysis Review(string group,string evidence)
        {
            return new Analysis{Status="needs-review",ReviewGroup=group,Evidence=evidence};
        }

        private static IList<string> DesignNotes(string name)
        {
            var notes=new List<string>();
            Action<string,string> add=(pattern,text)=>{if(Regex.IsMatch(name,pattern))notes.Add(text);};
            add(@"\btay dai\b","Thiết kế tay dài, có thể mặc riêng hoặc phối thêm áo khoác.");
            add(@"\bco v\b","Cổ chữ V tạo điểm nhấn cho phần cổ áo.");
            add(@"\boversize\b","Dáng oversize; gợi ý phối cùng phần quần gọn dáng để cân bằng tổng thể.");
            add(@"\bslim fit\b","Dáng slim fit hướng đến đường nét gọn; nên đối chiếu số đo để chọn độ ôm phù hợp.");
            add(@"\bong dung\b","Kiểu ống đứng với đường nét thẳng, dễ kết hợp cùng áo thun hoặc sơ mi.");
            add(@"\bong rong\b","Dáng ống rộng; có thể phối cùng phần áo gọn để tạo tổng thể cân đối.");
            add(@"\bong loe\b","Chi tiết ống loe tạo điểm nhấn cho phần chân quần.");
            add(@"\blung cao\b","Thiết kế lưng cao; gợi ý sơ vin áo để làm nổi bật phần eo.");
            add(@"\bxep ly\b","Chi tiết xếp ly tạo điểm nhấn cho thiết kế.");
            add(@"\bkhoa keo\b","Thiết kế khóa kéo, thuận tiện khi điều chỉnh cách mặc khoác ngoài.");
            add(@"\bhai hang khuy\b","Hai hàng khuy tạo điểm nhấn cho phần thân trước của áo.");
            add(@"\btui hop\b","Chi tiết túi hộp tạo điểm nhấn cho phong cách thường ngày.");
            add(@"\bkhong duong may\b","Thiết kế không đường may; chọn size theo số đo thực tế.");
            add(@"\b(rach goi|rach nhe|wash bac)\b","Chi tiết wash hoặc rách trang trí tạo sắc thái riêng cho trang phục jeans.");
            add(@"\bcrop\b","Dáng crop; có thể kết hợp với quần hoặc váy lưng cao.");
            add(@"\b(the thao|chay bo|gym|yoga|training|tennis|dap xe)\b","Phong cách thể thao; nên chọn kích cỡ phù hợp với cách vận động và mặc thử khi có thể.");
            add(@"\btron\b","Thiết kế trơn giúp bạn dễ kết hợp màu áo, quần và phụ kiện.");
            return notes.Take(2).ToList();
        }
    }
}
