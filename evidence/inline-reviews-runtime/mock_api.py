import json
from datetime import datetime, timezone, timedelta
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse, parse_qs

product = json.loads((Path(__file__).parents[1] / 'inline-review-product.json').read_text(encoding='utf-8-sig'))
state = {'review': None, 'reject': False, 'eligibility_error': False}
sample = {'SoSao': 5, 'NoiDung': 'Sản phẩm thoải mái, đúng mô tả.', 'TenHienThi': 'Khách thử nghiệm', 'NgayDanhGia': '2026-11-02T01:00:00Z'}

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

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
        path = url.path
        token = self.headers.get('Authorization', '').removeprefix('Bearer ')
        if path == '/api/products/SP-086':
            return self.reply(product)
        if path == '/api/products':
            return self.reply({'items': [], 'total': 0, 'page': int(query.get('page', ['1'])[0]), 'pageSize': int(query.get('pageSize', ['12'])[0])})
        if path == '/api/categories':
            return self.reply([product['category']])
        if path == '/api/account/me':
            return self.reply({'id': 'TEST-CUSTOMER', 'userName': token, 'fullName': 'Khách kiểm thử', 'rowVersion': 'AQIDBAUGBwg=', 'phone': '0900000001'})
        if path == '/api/cart':
            return self.reply({'items': [], 'totalQuantity': 0, 'subtotal': 0, 'canCheckout': False})
        if path.endswith('/eligible-orders'):
            if state['eligibility_error']:
                return self.reply({'title': 'Test service unavailable'}, 503)
            return self.reply([{'SoHD': 'HD-DELIVERED'}] if token == 'delivered' and state['review'] is None else [])
        if path.endswith('/mine'):
            return self.reply([state['review']] if token == 'delivered' and state['review'] else [])
        if path == '/api/customer/reviews/SP-086':
            rows = [dict(sample, TenHienThi='Khách thử %s' % i) for i in range(10 if query.get('page', ['1'])[0] == '1' else 1)]
            if state['review']:
                rows[0] = {key: state['review'][key] for key in sample}
            return self.reply(rows)
        return self.reply({'title': 'Fixture endpoint not found'}, 404)

    def do_POST(self):
        data = json.loads(self.rfile.read(int(self.headers.get('Content-Length', 0))) or b'{}')
        if self.path == '/_control':
            state.update(data)
            return self.reply({'ok': True})
        if self.path == '/api/account/login':
            return self.reply({'accessToken': data['UserName'], 'expiresAtUtc': (datetime.now(timezone.utc) + timedelta(hours=1)).isoformat()})
        if self.path == '/api/customer/reviews':
            token = self.headers.get('Authorization', '').removeprefix('Bearer ')
            if state['reject'] or token != 'delivered' or data.get('OrderId') != 'HD-DELIVERED' or state['review']:
                return self.reply({'title': 'Đơn chưa đủ điều kiện hoặc đã được đánh giá.'}, 409)
            state['review'] = dict(sample, SoSao=data['Stars'], NoiDung=data['Body'], SoHD='HD-DELIVERED', TrangThai='HIEN_THI')
            self.send_response(204)
            self.end_headers()
            return
        return self.reply({'title': 'Fixture endpoint not found'}, 404)

ThreadingHTTPServer(('127.0.0.1', 58473), Handler).serve_forever()
