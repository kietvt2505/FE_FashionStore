(function(){
    'use strict';
    var form=document.getElementById('customer-return-form'),data=document.getElementById('customer-return-options');
    if(!form||!data)return;
    var options=JSON.parse(data.textContent),itemSelect=form.querySelector('#sku'),kind=form.querySelector('#kind'),quantity=form.querySelector('#quantity');
    var color=form.querySelector('#return-color'),size=form.querySelector('#return-size'),newSku=form.querySelector('#newSku'),newQuantity=form.querySelector('#newQuantity'),submit=form.querySelector('#return-submit');
    var selectedItem=null;
    function setOptions(select,values,selected){select.textContent='';values.forEach(function(value){var option=document.createElement('option');option.value=value;option.textContent=value;select.appendChild(option);});select.value=values.indexOf(selected)>=0?selected:(values[0]||'');}
    function unique(values){return values.filter(function(value,index){return values.indexOf(value)===index;});}
    function updateSize(preferred){setOptions(size,unique(selectedItem.variants.filter(function(v){return v.color===color.value;}).map(function(v){return v.size;})),preferred);update();}
    function selectItem(reset){
        selectedItem=options.items.find(function(item){return item.sku===itemSelect.value;});if(!selectedItem){submit.disabled=true;return;}
        var variant=selectedItem.variants.find(function(v){return v.sku===(reset?selectedItem.sku:newSku.value);})||selectedItem.variants.find(function(v){return v.sku===selectedItem.sku;});
        if(reset){quantity.value=Math.min(selectedItem.purchasedQuantity,selectedItem.remainingQuantity);newQuantity.value=quantity.value;}
        quantity.max=selectedItem.remainingQuantity;
        document.getElementById('return-product-name').textContent=selectedItem.name;
        setOptions(color,unique(selectedItem.variants.map(function(v){return v.color;})),variant&&variant.color);updateSize(variant&&variant.size);
    }
    function update(){
        if(!selectedItem)return;
        var exchange=kind.value==='DOI_SAN_PHAM',variant=selectedItem.variants.find(function(v){return v.color===color.value&&v.size===size.value;});
        document.getElementById('exchange-fields').hidden=!exchange;newQuantity.disabled=!exchange;newQuantity.required=exchange;
        newQuantity.value=quantity.value;
        newSku.disabled=!exchange;newSku.value=variant?variant.sku:'';document.getElementById('return-selected-sku').textContent=variant?variant.sku:'Chưa có phân loại';
        var qty=Number(quantity.value),targetQty=exchange?Number(newQuantity.value):0,warning='';
        if(!Number.isInteger(qty)||qty<1||qty>selectedItem.remainingQuantity)warning='Chọn số lượng gửi lại từ 1 đến '+selectedItem.remainingQuantity+'.';
        else if(exchange&&(!variant||!variant.available))warning='Phân loại này đã ngừng bán. Vui lòng chọn màu/size còn hoạt động.';
        else if(exchange&&targetQty>variant.stock)warning='Phân loại này không đủ '+qty+' sản phẩm để đổi. Vui lòng chọn màu/size khác.';
        if(variant){newQuantity.max=variant.stock;document.getElementById('return-stock').textContent=variant.available?'Còn '+variant.stock+' sản phẩm':'Đã ngừng bán';}
        var warningElement=document.getElementById('return-form-warning');warningElement.textContent=warning;warningElement.style.display=warning?'':'none';submit.disabled=!!warning;
    }
    itemSelect.addEventListener('change',function(){selectItem(true);});color.addEventListener('change',function(){updateSize(size.value);});size.addEventListener('change',update);kind.addEventListener('change',update);quantity.addEventListener('input',update);newQuantity.addEventListener('input',update);
    ['accountHolder','accountNumber','bankName'].forEach(function(id){document.getElementById(id).addEventListener('input',function(){var inputs=['accountHolder','accountNumber','bankName'].map(function(key){return document.getElementById(key);}),any=inputs.some(function(input){return input.value.trim().length>0;});inputs.forEach(function(input){input.required=any;});});});
    form.addEventListener('submit',function(event){update();if(submit.disabled){event.preventDefault();return;}submit.disabled=true;submit.textContent='ĐANG GỬI YÊU CẦU…';form.setAttribute('aria-busy','true');});
    selectItem(false);
})();
