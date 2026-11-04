(function(){'use strict';
 var dialog=document.getElementById('guest-merge-dialog');
 if(dialog&&dialog.showModal){
  if(dialog.dataset.autoOpen==='true')dialog.showModal();
  document.querySelectorAll('[data-open-guest-merge]').forEach(function(button){button.addEventListener('click',function(){if(!dialog.open)dialog.showModal();});});
  dialog.querySelectorAll('[data-close-guest-merge]').forEach(function(button){button.addEventListener('click',function(){dialog.close();});});
  dialog.querySelector('form').addEventListener('submit',function(event){
   var form=event.currentTarget;if(form.dataset.submitting==='true'){event.preventDefault();return;}
   form.dataset.submitting='true';form.setAttribute('aria-busy','true');
   if(event.submitter)event.submitter.textContent=event.submitter.value==='true'?'Đang gộp giỏ…':'Đang xử lý…';
  });
 }
 document.querySelectorAll('.yody-cart-update').forEach(function(form){
  var input=form.querySelector('[name="quantity"]');
  function clearError(){input.removeAttribute('aria-invalid');var error=form.querySelector('.quantity-error');if(error)error.remove();}
  function highlight(){input.focus();input.select();input.setAttribute('aria-invalid','true');}
  if(input.dataset.focusQuantity==='true')highlight();
  form.addEventListener('submit',function(event){var quantity=Number(input.value),max=Number(input.dataset.max);
   if(!/^\d+$/.test(input.value)||!Number.isSafeInteger(quantity)||quantity<1||quantity>max){event.preventDefault();
    var error=form.querySelector('.quantity-error');if(!error){error=document.createElement('p');error.className='quantity-error';error.setAttribute('role','alert');form.appendChild(error);}error.textContent='Số lượng phải từ 1 đến '+max+'.';highlight();}
  });input.addEventListener('input',clearError);
 });
 var wishlistForms=Array.from(document.querySelectorAll('.wishlist-form, [data-wishlist-return]'));
 var wishlistNotice=document.createElement('p');wishlistNotice.className='yody-wishlist-notice';wishlistNotice.setAttribute('role','status');wishlistNotice.setAttribute('aria-live','polite');wishlistNotice.hidden=true;document.body.appendChild(wishlistNotice);
 var wishlistNoticeTimer;
 function wishlistMessage(message){wishlistNotice.textContent=message;wishlistNotice.hidden=false;clearTimeout(wishlistNoticeTimer);wishlistNoticeTimer=setTimeout(function(){wishlistNotice.hidden=true;},5000);}
 wishlistForms.forEach(function(form){form.addEventListener('submit',async function(event){
  var returnUrl=form.querySelector('[name="returnUrl"]');
  if(returnUrl)returnUrl.value=window.location.pathname+window.location.search+window.location.hash;
  if(!window.fetch)return;
  event.preventDefault();
  var id=form.querySelector('[name="id"]').value;
  var related=wishlistForms.filter(function(other){return other.querySelector('[name="id"]').value===id;});
  if(related.some(function(other){return other.dataset.busy==='true';}))return;
  related.forEach(function(other){other.dataset.busy='true';other.querySelector('button').disabled=true;other.setAttribute('aria-busy','true');});
  try{
   var response=await fetch(form.action,{method:'POST',credentials:'same-origin',headers:{'X-Requested-With':'XMLHttpRequest'},body:new FormData(form)});
   var result=await response.json();
   if(result.loginUrl){window.location.assign(result.loginUrl);return;}
   if(!response.ok||typeof result.saved!=='boolean')throw new Error(result.error||'Chưa thể cập nhật yêu thích. Vui lòng thử lại.');
   related.forEach(function(other){var button=other.querySelector('button');button.setAttribute('aria-pressed',String(result.saved));button.setAttribute('aria-label',result.saved?'Bỏ yêu thích':'Lưu yêu thích');button.title=result.saved?'Bỏ yêu thích':'Lưu yêu thích';if(button.hasAttribute('data-wishlist-label'))button.textContent=result.saved?'♥ Đã yêu thích':'♡ Lưu yêu thích';});
   wishlistMessage(result.message);
  }catch(error){wishlistMessage(error.message||'Chưa thể cập nhật yêu thích. Vui lòng thử lại.');}
  finally{related.forEach(function(other){delete other.dataset.busy;other.removeAttribute('aria-busy');other.querySelector('button').disabled=false;});}
 });});
 var frame=document.querySelector('.product-zoom'),image=document.getElementById('detail-main-image');
 if(frame&&image){function canZoom(){return !image.hidden&&!!image.getAttribute('src')&&image.naturalWidth>0&&frame.dataset.empty!=='true';}frame.addEventListener('pointermove',function(event){if(event.pointerType==='touch'||!canZoom())return;var r=frame.getBoundingClientRect();image.style.transformOrigin=((event.clientX-r.left)/r.width*100)+'% '+((event.clientY-r.top)/r.height*100)+'%';frame.classList.add('is-zoomed');});
  frame.addEventListener('pointerleave',function(){frame.classList.remove('is-zoomed');});frame.addEventListener('click',function(){if(canZoom())frame.classList.toggle('is-zoomed');});frame.addEventListener('keydown',function(event){if((event.key==='Enter'||event.key===' ')&&canZoom()){event.preventDefault();frame.classList.toggle('is-zoomed');}if(event.key==='Escape')frame.classList.remove('is-zoomed');});
 }
 document.querySelectorAll('[data-suggest-color]').forEach(function(button){button.addEventListener('click',function(){var match=Array.from(document.querySelectorAll('[data-color-choice]')).find(function(choice){return choice.dataset.colorChoice===button.dataset.suggestColor;});if(match){match.click();document.querySelector('.yody-product-purchase').scrollIntoView({behavior:'smooth',block:'start'});}});});
}());
