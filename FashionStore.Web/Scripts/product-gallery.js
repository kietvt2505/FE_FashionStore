(function(){
    'use strict';
    var root=document.querySelector('[data-product-gallery]');if(!root)return;
    var frame=root.querySelector('[data-gallery-frame]'),image=root.querySelector('#detail-main-image'),hint=root.querySelector('.zoom-hint');
    var all=Array.from(root.querySelectorAll('[data-managed-gallery]')),current=[],index=0;
    var previous=root.querySelector('[data-gallery-prev]'),next=root.querySelector('[data-gallery-next]'),caption=root.querySelector('[data-gallery-caption]');
    function empty(value){image.hidden=value;frame.dataset.empty=String(value);frame.tabIndex=value?-1:0;hint.hidden=value;frame.classList.remove('is-zoomed');}
    function select(position,scroll){
        if(!current.length)return;
        index=Math.max(0,Math.min(position,current.length-1));var selected=current[index];
        all.forEach(function(button){var active=button===selected;button.classList.toggle('active',active);button.setAttribute('aria-pressed',String(active));});
        empty(true);image.style.transformOrigin='center';image.alt=selected.dataset.galleryAlt;
        var url=selected.dataset.galleryImage;
        caption.textContent='Góc chụp '+(index+1)+' / '+current.length+(url?'':' — chưa có ảnh');
        if(url){image.src=url;if(image.complete&&image.naturalWidth>0)empty(false);}
        else image.removeAttribute('src');
        previous.disabled=index===0;next.disabled=index===current.length-1;
        if(scroll)selected.scrollIntoView({block:'nearest',inline:'nearest'});
    }
    function color(value){
        current=all.filter(function(button){var active=String(button.dataset.galleryColor||'').trim().toLowerCase()===String(value||'').trim().toLowerCase();button.hidden=!active;return active;});
        select(0,false);
    }
    all.forEach(function(button){button.addEventListener('click',function(){select(current.indexOf(button),false);});});
    image.addEventListener('load',function(){if(image.getAttribute('src'))empty(false);});
    image.addEventListener('error',function(){empty(true);caption.textContent='Góc chụp '+(index+1)+' — ảnh đang được cập nhật';});
    document.querySelectorAll('[data-managed-image]:not([data-card-image]):not(#detail-main-image)').forEach(function(photo){
        function fail(){photo.hidden=true;}
        photo.addEventListener('error',fail);if(photo.currentSrc&&photo.complete&&photo.naturalWidth===0)fail();
    });
    previous.addEventListener('click',function(){select(index-1,true);});next.addEventListener('click',function(){select(index+1,true);});
    root.addEventListener('keydown',function(event){if(event.key==='ArrowLeft'||event.key==='ArrowRight'){event.preventDefault();select(index+(event.key==='ArrowRight'?1:-1),true);}});
    root.addEventListener('product:color',function(event){color(event.detail.color);});
    color(root.dataset.initialColor);
}());
