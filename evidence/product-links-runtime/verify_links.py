import html
import json
import re
from html.parser import HTMLParser
from http.cookiejar import CookieJar
from pathlib import Path
from urllib.parse import urlsplit, parse_qs, urlencode
from urllib.request import build_opener, HTTPCookieProcessor

root = Path(__file__).parent
products = [json.loads((root / 'product.json').read_text(encoding='utf-8-sig')),
            json.loads((root.parent / 'inline-review-product.json').read_text(encoding='utf-8-sig'))]
base = 'http://localhost:58474'
client = build_opener(HTTPCookieProcessor(CookieJar()))

def fetch(path, data=None):
    with client.open(base + path, None if data is None else urlencode(data).encode(), timeout=30) as response:
        return response.geturl(), response.read().decode()

def fields(document):
    values = {}
    for tag in re.findall(r'<input\b[^>]*>', document):
        name, value = re.search(r'name="([^"]*)"', tag), re.search(r'value="([^"]*)"', tag)
        if name: values[name[1]] = html.unescape(value[1] if value else '')
    return values

class Links(HTMLParser):
    def __init__(self, document):
        super().__init__()
        self.links, self.active = [], None
        self.nested = False
        self.feed(document)
    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'a':
            self.nested |= self.active is not None
            self.active = dict(attrs, text='', controls=[])
            self.links.append(self.active)
        elif self.active is not None and tag in ['button', 'input', 'select', 'form']:
            self.active['controls'].append(tag)
    def handle_endtag(self, tag):
        if tag == 'a': self.active = None
    def handle_data(self, data):
        if self.active is not None: self.active['text'] += data

_, page = fetch('/Account/Login?returnUrl=%2FOrders')
data = fields(page)
data.update(UserName='test', Password='SyntheticProductLinks123!', ReturnUrl='/Orders')
fetch('/Account/Login', data)
results = {}
for path in ['/Cart', '/Checkout', '/Orders/Details/HD-NEW', '/Orders/Details/HD-DELIVERED', '/Orders/Details/HD-CANCELLED', '/Customer/Wishlist']:
    _, page = fetch(path)
    parsed = Links(page)
    assert not parsed.nested, path
    entries = [a for a in parsed.links if 'product-entry-link' in a.get('class', '') and a.get('href')]
    assert len(entries) == 2, (path, len(entries))
    for entry, product in zip(entries, products):
        url = urlsplit(entry['href'])
        assert url.path == '/Product/Details/' + product['id'], (path, entry)
        assert product['name'] in entry['text']
        assert not entry['controls'], (path, entry)
        if path != '/Customer/Wishlist': assert 'color' in parse_qs(url.query), path
        destination, detail = fetch(entry['href'])
        assert urlsplit(destination).path == url.path
        assert product['name'] in html.unescape(detail) and 'id="detail-product-name"' in detail
    if 'HD-DELIVERED' in path:
        review_links = [a for a in parsed.links if 'Viết đánh giá' in a['text']]
        assert len(review_links) == 2 and all(urlsplit(a['href']).fragment == 'product-reviews' for a in review_links)
    if path == '/Cart': assert 'class="yody-cart-update"' in page and 'class="yody-cart-remove"' in page
    if path == '/Customer/Wishlist': assert not any('INACTIVE' in a.get('href', '') for a in parsed.links)
    results[path] = dict(productLinks=len(entries), correctProductAndColor=True, independentControls=True)

for path in ['/Orders', '/Orders?status=HUY']:
    _, page = fetch(path)
    order_links = [a for a in Links(page).links if a.get('href', '').startswith('/Orders/Details/')]
    assert len(order_links) == 3
    results[path] = dict(orderLinksPreserved=True)

root.joinpath('results.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(results, ensure_ascii=False, indent=2))
