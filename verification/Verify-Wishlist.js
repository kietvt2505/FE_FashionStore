// Runs browser event handlers without touching accounts, cookies or a real database.
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
function element(extra={}){return Object.assign({dataset:{},attributes:{},listeners:{},hidden:false,disabled:false,
 addEventListener(name,handler){this.listeners[name]=handler;},setAttribute(k,v){this.attributes[k]=v;},removeAttribute(k){delete this.attributes[k];},hasAttribute(k){return k in this.attributes;}},extra);}
function form(id,label=false){const button=element(),product={value:id},origin={value:'/Product'};if(label)button.attributes['data-wishlist-label']='';return element({action:'/Customer/SaveWishlist',button,origin,querySelector(selector){return {'button':button,'[name="id"]':product,'[name="returnUrl"]':origin}[selector];}});}
const forms=[form('SP-001'),form('SP-001',true),form('SP-002')],notices=[],navigations=[],calls=[];
let respond;
const document={body:{appendChild(node){notices.push(node);}},createElement(){return element();},getElementById(){return null;},querySelector(){return null;},querySelectorAll(selector){return selector==='.wishlist-form, [data-wishlist-return]'?forms:[];}};
const window={fetch:true,location:{pathname:'/Product',search:'?audience=NAM&page=3',hash:'#catalog',assign(url){navigations.push(url);}}};
vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../FashionStore.Web/Scripts/storefront.js'),'utf8'),{
 document,window,FormData:class{constructor(form){this.form=form;}},setTimeout(){return 1;},clearTimeout(){},
 fetch(url,options){calls.push({url,options});return new Promise(resolve=>{respond=(result,ok=true)=>resolve({ok,json:async()=>result});});}
});
async function submit(index,result,ok=true){let prevented=false;const pending=forms[index].listeners.submit({preventDefault(){prevented=true;}});assert.equal(prevented,true);respond(result,ok);await pending;}
(async()=>{
 let prevented=false;const first=forms[0].listeners.submit({preventDefault(){prevented=true;}});assert.equal(prevented,true);
 assert.equal(forms[0].button.disabled,true);assert.equal(forms[1].button.disabled,true);assert.equal(forms[2].button.disabled,false);
 await forms[1].listeners.submit({preventDefault(){}});assert.equal(calls.length,1);
 respond({saved:true,message:'Đã thêm'});await first;
 assert.equal(forms[0].button.attributes['aria-pressed'],'true');assert.equal(forms[1].button.textContent,'♥ Đã yêu thích');
 assert.deepEqual(navigations,[]);assert.equal(forms[0].origin.value,'/Product?audience=NAM&page=3#catalog');
 assert.equal(calls[0].options.headers['X-Requested-With'],'XMLHttpRequest');assert.equal(calls[0].options.credentials,'same-origin');
 await submit(1,{saved:false,message:'Đã bỏ'});assert.equal(forms[0].button.attributes['aria-pressed'],'false');assert.equal(forms[1].button.textContent,'♡ Lưu yêu thích');
 await submit(0,{error:'Chưa cập nhật được'},false);assert.equal(notices[0].textContent,'Chưa cập nhật được');assert.equal(forms[0].button.attributes['aria-pressed'],'false');assert.equal(forms[0].button.disabled,false);
 await submit(0,{loginUrl:'/Account/Login?returnUrl=%2FProduct'});assert.deepEqual(navigations,['/Account/Login?returnUrl=%2FProduct']);
 assert.equal(forms[1].button.disabled,false);console.log('Wishlist verified: add/remove in place, duplicate click prevention, synchronized buttons, error recovery, guest login return.');
})().catch(error=>{console.error(error);process.exitCode=1;});
