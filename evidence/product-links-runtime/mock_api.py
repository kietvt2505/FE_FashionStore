import json
from datetime import datetime, timezone, timedelta
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse, parse_qs

root = Path(__file__).parent
products = [json.loads((root / 'product.json').read_text(encoding='utf-8-sig')),
            json.loads((root.parent / 'inline-review-product.json').read_text(encoding='utf-8-sig'))]
lines = []
for product in products:
    variant = next((v for v in product['variants'] if v['color'] == 'Đen' and v['size'] == 'M'), product['variants'][0])
    lines.append(dict(productId=product['id'], productName=product['name'], name=product['name'], imageUrl=product['imageUrl'],
                      sku=variant['sku'], size=variant['size'], color=variant['color'], quantity=2,
                      unitPrice=variant['price'], lineTotal=variant['price'] * 2, total=variant['price'] * 2,
                      stock=variant['stock'], available=True))
total = sum(line['lineTotal'] for line in lines)

def order(order_id):
    status = 'HUY' if order_id == 'HD-CANCELLED' else 'HOAN_TAT' if order_id == 'HD-DELIVERED' else 'MOI'
    return dict(orderId=order_id, createdAt='2026-11-03T01:00:00Z', status=status,
                shippingStatus='DA_GIAO' if status == 'HOAN_TAT' else '', paymentStatus='DA_THANH_TOAN' if status == 'HOAN_TAT' else 'CHUA_THANH_TOAN',
                paymentMethod='COD', recipient='Khách kiểm thử', phone='0900000001', address='Địa chỉ kiểm thử',
                total=total, subtotal=total, supportsHistory=False, supportsReturns=False, canCancel=False, items=lines)

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args): pass
    def reply(self, payload, status=200):
        content = json.dumps(payload, ensure_ascii=False).encode('utf-8')
        self.send_response(status)
        self.send_header('Content-Type', 'application/json; charset=utf-8')
        self.send_header('Content-Length', str(len(content)))
        self.end_headers()
        self.wfile.write(content)
    def do_GET(self):
        url = urlparse(self.path)
        query = parse_qs(url.query)
        if url.path.startswith('/api/products/'):
            return self.reply(next(p for p in products if p['id'] == url.path.rsplit('/', 1)[1]))
        if url.path == '/api/products':
            return self.reply(dict(items=[], total=0, page=int(query.get('page', ['1'])[0]), pageSize=int(query.get('pageSize', ['12'])[0])))
        if url.path == '/api/categories': return self.reply([p['category'] for p in products])
        if url.path == '/api/account/me': return self.reply(dict(id='TEST', userName='test', fullName='Khách kiểm thử', rowVersion='AQIDBAUGBwg=', phone='0900000001'))
        if url.path == '/api/cart': return self.reply(dict(items=lines, totalQuantity=4, subtotal=total, canCheckout=True, checkoutEnabled=True))
        if url.path == '/api/orders/quote': return self.reply(dict(discount=0, total=total))
        if url.path == '/api/payments/options': return self.reply([])
        if url.path == '/api/orders': return self.reply([order(i) for i in ['HD-NEW', 'HD-DELIVERED', 'HD-CANCELLED']])
        if url.path.startswith('/api/orders/'): return self.reply(order(url.path.rsplit('/', 1)[1]))
        if url.path == '/api/customer/wishlist':
            return self.reply([dict(MaSP=p['id'], TenSanPham=p['name'], ImageUrl=p['imageUrl'], TrangThai=True) for p in products] +
                              [dict(MaSP='INACTIVE', TenSanPham='Sản phẩm ngừng kinh doanh', TrangThai=False)])
        if url.path.startswith('/api/customer/reviews/'): return self.reply([])
        return self.reply(dict(title='Fixture endpoint not found'), 404)
    def do_POST(self):
        data = json.loads(self.rfile.read(int(self.headers.get('Content-Length', 0))) or b'{}')
        if self.path == '/api/account/login':
            return self.reply(dict(accessToken='synthetic-test', expiresAtUtc=(datetime.now(timezone.utc) + timedelta(hours=1)).isoformat()))
        return self.reply(dict(title='No mutations allowed in product link fixture'), 405)

ThreadingHTTPServer(('127.0.0.1', 58473), Handler).serve_forever()
