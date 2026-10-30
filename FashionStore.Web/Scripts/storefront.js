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
  function highlight(){input.focus();input.select();input.setAttribute('aria-invalid','true');}
  if(input.dataset.focusQuantity==='true')highlight();
  form.addEventListener('submit',function(event){var quantity=Number(input.value),max=Number(input.dataset.max);
   if(!/^\d+$/.test(input.value)||!Number.isSafeInteger(quantity)||quantity<1||quantity>max){event.preventDefault();
    var error=form.querySelector('.quantity-error');if(!error){error=document.createElement('p');error.className='quantity-error';error.setAttribute('role','alert');form.appendChild(error);}error.textContent='Số lượng phải từ 1 đến '+max+'.';highlight();}
  });input.addEventListener('input',function(){input.removeAttribute('aria-invalid');});
 });
 var frame=document.querySelector('.product-zoom'),image=document.getElementById('detail-main-image');
 if(frame&&image){frame.addEventListener('pointermove',function(event){if(event.pointerType==='touch')return;var r=frame.getBoundingClientRect();image.style.transformOrigin=((event.clientX-r.left)/r.width*100)+'% '+((event.clientY-r.top)/r.height*100)+'%';frame.classList.add('is-zoomed');});
  frame.addEventListener('pointerleave',function(){frame.classList.remove('is-zoomed');});frame.addEventListener('click',function(){frame.classList.toggle('is-zoomed');});frame.addEventListener('keydown',function(event){if(event.key==='Enter'||event.key===' '){event.preventDefault();frame.classList.toggle('is-zoomed');}if(event.key==='Escape')frame.classList.remove('is-zoomed');});
 }
 document.querySelectorAll('[data-suggest-color]').forEach(function(button){button.addEventListener('click',function(){var match=Array.from(document.querySelectorAll('[data-color-choice]')).find(function(choice){return choice.dataset.colorChoice===button.dataset.suggestColor;});if(match){match.click();document.querySelector('.yody-product-purchase').scrollIntoView({behavior:'smooth',block:'start'});}});});
}());
