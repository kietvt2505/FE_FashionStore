import http from 'node:http';
import fs from 'node:fs';
const records=[];
const variants=[{sku:'TEE-BLK-S',color:'Đen',size:'S',unitPrice:100000,stock:20,available:true},{sku:'TEE-BLK-M',color:'Đen',size:'M',unitPrice:100000,stock:18,available:true},{sku:'TEE-BLU-S',color:'Xanh',size:'S',unitPrice:100000,stock:8,available:true},{sku:'TEE-BLU-M',color:'Xanh',size:'M',unitPrice:100000,stock:1,available:true}];
const options={orderId:'WEB-RETURN-PREVIEW',recipient:'Nguyễn Văn An',phone:'0900000002',address:'123 Đường kiểm thử, TP. Hồ Chí Minh',deliveredAt:'2026-11-04T10:00:00',deadline:'2026-11-19T10:00:00',vatRate:8,items:[{sku:'TEE-BLK-S',productId:'TEE',name:'Áo thun Nam',color:'Đen',size:'S',purchasedQuantity:2,remainingQuantity:2,processedQuantity:0,paidTotal:216000,processedValue:0,variants}]};
const slips=[{SoPhieuDT:'WR-PREVIEW-APPROVED',SoHD:options.orderId,NgayLap:'2026-11-04T10:00:00',HinhThucXuLy:'HOAN_TIEN',TrangThai:'HOAN_TAT',LyDo:'Trả hàng không vừa',SoTienHoan:216000,SoTienThuThem:0,DaKiem:1,SoLuongTra:2,SanPham:'Áo thun Nam · Đen · S × 2',TaiKhoanAn:null}];
http.createServer(async(req,res)=>{let raw='';for await(const chunk of req)raw+=chunk;const body=raw?JSON.parse(raw):{},url=new URL(req.url,'http://localhost');
 const reply=(value,status=200)=>{res.writeHead(status,{'Content-Type':'application/json; charset=utf-8'});res.end(status===204?'':JSON.stringify(value));};
 if(req.method==='POST'&&url.pathname==='/api/account/login')return reply({accessToken:'local-ui-preview',expiresAtUtc:new Date(Date.now()+3600000).toISOString()});
 if(url.pathname==='/api/account/me')return reply({id:'PREVIEW',userName:'return-preview',fullName:options.recipient,phone:options.phone,email:'preview@example.test',rowVersion:'preview'});
 if(url.pathname==='/api/cart')return reply({items:[],totalQuantity:0,subtotal:0,canCheckout:false});
 if(url.pathname==='/api/categories')return reply([]);
 if(req.method==='GET'&&url.pathname==='/api/customer/returns/options/'+options.orderId)return reply(options);
 if(req.method==='GET'&&url.pathname==='/api/customer/returns')return reply(slips);
 if(req.method==='POST'&&url.pathname==='/api/customer/returns'){
   records.push(body);fs.writeFileSync('tmp/web-return-preview-submissions.json',JSON.stringify(records,null,2));
   if(body.Reason==='Kiểm tra giữ dữ liệu lỗi')return reply({title:'Phân loại vừa hết hàng, vui lòng chọn lại.'},409);
   slips.unshift({SoPhieuDT:'WR-PREVIEW-'+records.length,SoHD:body.OrderId,NgayLap:new Date().toISOString(),HinhThucXuLy:body.Kind,TrangThai:'CHO_DUYET',LyDo:body.Reason,SanPham:'Áo thun Nam · Đen · S × '+body.Quantity+' → '+body.NewSku+' × '+body.NewQuantity,SoTienHoan:0,SoTienThuThem:0});return reply(null,204);
 }
 if(req.method==='POST'&&url.pathname==='/api/customer/returns/WR-PREVIEW-APPROVED/refund-account'){fs.writeFileSync('tmp/web-return-preview-bank.json',JSON.stringify(body));return reply(null,204);}
 reply({title:'Preview endpoint not configured'},404);
}).listen(5039,'127.0.0.1');
