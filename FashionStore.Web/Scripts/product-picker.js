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
    var gallery=document.querySelector('[data-product-gallery]');
    function sameColor(a,b){return String(a||'').trim().toLowerCase()===String(b||'').trim().toLowerCase();}
    function available(v) { return Number(v.dataset.stock) > 0; }
    function matches(v) { return v.dataset.size === size && sameColor(v.dataset.color,color) && available(v); }
    function update() {
        colors.forEach(function (button) {
            var soldOut=!variants.some(function(v){return available(v)&&sameColor(v.dataset.color,button.dataset.colorChoice);});
            // Out-of-stock colors can still be previewed; only purchasing is disabled.
            button.disabled=false;button.classList.toggle('is-sold-out',soldOut);
            button.title=button.dataset.colorChoice+(soldOut?' — tạm hết hàng':'');button.setAttribute('aria-label',button.title);
            button.setAttribute("aria-pressed", String(sameColor(button.dataset.colorChoice,color)));
        });
        sizes.forEach(function (button) {
            button.disabled = !variants.some(function (v) { return available(v) && v.dataset.size === button.dataset.sizeChoice && (!color || sameColor(v.dataset.color,color)); });
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
        var colorVariants=variants.filter(function(v){return sameColor(v.dataset.color,color);});
        var priced=variant||colorVariants.filter(available).sort(function(a,b){return Number(a.dataset.price)-Number(b.dataset.price);})[0]||colorVariants.slice().sort(function(a,b){return Number(a.dataset.price)-Number(b.dataset.price);})[0];
        price.textContent = priced ? (variant?'':'Từ ')+priced.dataset.displayPrice : initialPrice;
        if(original){var base=priced?priced.dataset.originalPrice:initialOriginal;original.hidden=!base;original.querySelector('del').textContent=base||'';}
        var soldOut=!variants.some(available);
        var colorSoldOut=!!color&&!colorVariants.some(available);
        stock.textContent = variant ? "● Còn " + variant.dataset.stock + " sản phẩm" : soldOut?"● Sản phẩm đã hết hàng":colorSoldOut?"● Màu "+color+" tạm hết hàng":"Chọn phân loại để xem tồn kho";
        stock.classList.toggle("in-stock", !!variant);
        stock.classList.toggle("out-of-stock",soldOut||colorSoldOut);
        document.querySelectorAll('[data-suggest-color]').forEach(function(button){button.hidden=sameColor(button.dataset.suggestColor,color);});
    }
    sizes.forEach(function (button) {
        button.addEventListener("click", function () {
            size = button.dataset.sizeChoice;
            update();
        });
    });
    colors.forEach(function (button) {
        button.addEventListener("click", function () {
            color = button.dataset.colorChoice;
            if (size && !variants.some(matches)) size = "";
            update();
            if(gallery)gallery.dispatchEvent(new CustomEvent('product:color',{detail:{color:color}}));
            if(window.URL&&window.history.replaceState){var url=new URL(window.location.href);url.searchParams.set('color',color);window.history.replaceState(null,'',url.toString());}
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
    var firstColor=Array.prototype.find.call(colors,function(button){return sameColor(button.dataset.colorChoice,picker.dataset.initialColor);})||Array.prototype.find.call(colors,function(button){return variants.some(function(v){return available(v)&&sameColor(v.dataset.color,button.dataset.colorChoice);});})||colors[0];
    if (firstColor) firstColor.click();else update();
}());
