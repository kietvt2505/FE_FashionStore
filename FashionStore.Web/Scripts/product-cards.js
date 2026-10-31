(function(){
    "use strict";
    document.querySelectorAll('[data-product-card]').forEach(function(card){
        var image=card.querySelector('[data-card-image]');
        function hideMissing(){image.hidden=true;}
        if(image){
            image.addEventListener('error',hideMissing);
            image.addEventListener('load',function(){image.hidden=!image.getAttribute('src')||image.naturalWidth===0;});
            // A lazy image can be complete before its request starts; hiding it then prevents loading.
            if(image.currentSrc&&image.complete&&image.naturalWidth===0)hideMissing();
        }
        var buttons=card.querySelectorAll('[data-card-color]');
        buttons.forEach(function(button){button.addEventListener('click',function(){
            buttons.forEach(function(other){other.setAttribute('aria-pressed',String(other===button));});
            var color=button.dataset.cardColor;
            if(image){
                image.hidden=true;image.alt=card.dataset.productName+' - '+color;
                if(button.dataset.cardPhoto){image.loading='eager';image.src=button.dataset.cardPhoto;if(image.complete&&image.naturalWidth>0)image.hidden=false;}
                else image.removeAttribute('src');
            }
            card.querySelectorAll('[data-product-details]').forEach(function(link){link.href=button.dataset.cardHref;});
            var price=card.querySelector('[data-card-price]'),original=card.querySelector('[data-card-original]');
            var discounted=Number(button.dataset.cardPercent)>0;
            price.textContent=button.dataset.cardDisplayPrice;price.classList.toggle('sale-price',discounted);
            original.textContent=button.dataset.cardDisplayOriginal;original.hidden=!discounted;
            var badge=card.querySelector('[data-card-discount]');badge.textContent='−'+button.dataset.cardPercent+'%';badge.hidden=!discounted;
            var available=Number(button.dataset.cardStock)>0;
            var availability=card.querySelector('[data-card-availability]');availability.hidden=available;availability.textContent='Hết màu này';
            card.querySelector('[data-card-stock-note]').textContent=available?'Có sẵn tại cửa hàng':'Màu này tạm hết hàng';
            card.querySelector('[data-card-color-name]').textContent=color+(available?'':' — tạm hết hàng');
        });});
    });
}());
