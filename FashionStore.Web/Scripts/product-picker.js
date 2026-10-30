/* Separate size/color choices still submit the exact existing selected-sku field. */
(function () {
    "use strict";
    var select = document.getElementById("sku-select");
    var picker = document.getElementById("sku-picker");
    if (!select || !picker) return;
    var variants = Array.prototype.filter.call(select.options, function (o) { return !!o.value; });
    var sizes = picker.querySelectorAll("[data-size-choice]");
    var colors = picker.querySelectorAll("[data-color-choice]");
    var form = select.form;
    var cart = form.querySelector(".detail-cart");
    var quantity = document.getElementById("detail-quantity");
    var price = document.getElementById("detail-price");
    var stock = document.getElementById("detail-stock");
    var label = document.getElementById("selected-sku-label");
    var colorLabel = document.getElementById("selected-color-label");
    var initialPrice = price.textContent;
    var original=document.getElementById('detail-original-price'),initialOriginal=original?original.querySelector('del').textContent:'';
    var size = "", color = "";
    function available(v) { return Number(v.dataset.stock) > 0; }
    function matches(v) { return v.dataset.size === size && v.dataset.color === color && available(v); }
    function update() {
        colors.forEach(function (button) {
            button.disabled = !variants.some(function (v) {
                return available(v) && v.dataset.color === button.dataset.colorChoice;
            });
            button.setAttribute("aria-pressed", String(button.dataset.colorChoice === color));
        });
        sizes.forEach(function (button) {
            button.disabled = !variants.some(function (v) { return available(v) && v.dataset.size === button.dataset.sizeChoice && (!color || v.dataset.color === color); });
            button.setAttribute("aria-pressed", String(button.dataset.sizeChoice === size));
            var note=button.querySelector('.size-unavailable');if(note)note.hidden=!button.disabled;
            button.title=button.dataset.sizeChoice+(button.disabled?' — hết size'+(color?' màu '+color:''):'');
            button.setAttribute('aria-label',button.title);
        });
        var variant = variants.find(matches);
        select.value = variant ? variant.value : "";
        cart.disabled = !variant;
        quantity.disabled = !variant;
        quantity.max = variant ? variant.dataset.stock : "1";
        quantity.value = String(Math.max(1, Math.min(Number(quantity.value) || 1, Number(quantity.max))));
        colorLabel.textContent = color || "Chưa chọn màu";
        label.textContent = variant ? size + " · " + color : color ? "Vui lòng chọn kích cỡ" : "Vui lòng chọn màu sắc và kích cỡ";
        var priced=variant||variants.filter(function(v){return available(v)&&v.dataset.color===color;}).sort(function(a,b){return Number(a.dataset.price)-Number(b.dataset.price);})[0];
        price.textContent = priced ? (variant?'':'Từ ')+priced.dataset.displayPrice : initialPrice;
        if(original){var base=priced?priced.dataset.originalPrice:initialOriginal;original.hidden=!base;original.querySelector('del').textContent=base||'';}
        var soldOut=!variants.some(available);
        stock.textContent = variant ? "● Còn " + variant.dataset.stock + " sản phẩm" : soldOut?"● Sản phẩm đã hết hàng":"Chọn phân loại để xem tồn kho";
        stock.classList.toggle("in-stock", !!variant);
        stock.classList.toggle("out-of-stock",soldOut);
        document.querySelectorAll('[data-suggest-color]').forEach(function(button){button.hidden=button.dataset.suggestColor===color;});
    }
    sizes.forEach(function (button) {
        button.addEventListener("click", function () {
            size = button.dataset.sizeChoice;
            if (color && !variants.some(matches)) color = "";
            update();
        });
    });
    colors.forEach(function (button) {
        button.addEventListener("click", function () {
            color = button.dataset.colorChoice;
            if (size && !variants.some(matches)) size = "";
            update();
            var thumbnail = Array.prototype.find.call(document.querySelectorAll("[data-gallery-color]"), function (b) {
                return b.dataset.galleryColor === color;
            });
            if (thumbnail) thumbnail.click();
            else {
                var main = document.getElementById("detail-main-image");
                if (main) { main.src = button.dataset.colorImage; main.alt = button.querySelector("img").alt; }
            }
        });
    });
    form.addEventListener("submit", function (event) {
        if (!variants.some(function (v) { return v.value === select.value && matches(v); })) {
            event.preventDefault();
            label.textContent = "Vui lòng chọn kích cỡ và màu sắc còn hàng.";
        }
    });
    select.required = false;
    document.getElementById("sku-fallback").hidden = true;
    picker.hidden = false;
    update();
    var firstColor = Array.prototype.find.call(colors, function (button) { return !button.disabled; });
    if (firstColor) firstColor.click();
}());
