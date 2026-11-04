import html
import json
import re
from http.cookiejar import CookieJar
from pathlib import Path
from urllib.parse import urlencode, urlsplit, parse_qs
from urllib.request import build_opener, HTTPCookieProcessor, Request, urlopen

base = 'http://localhost:58474'
origin = '/Product/Details/SP-086?' + urlencode({'color': 'Đỏ', 'returnUrl': '/Product?page=8', 'reviewOrderId': 'HD-DELIVERED'})
results = {}

def fetch(client, path, data=None):
    with client.open(base + path, None if data is None else urlencode(data).encode(), timeout=40) as response:
        return response.geturl(), html.unescape(response.read().decode())

def fields(document):
    values = {}
    for tag in re.findall(r'<input\b[^>]*>', document):
        name = re.search(r'name="([^"]*)"', tag)
        value = re.search(r'value="([^"]*)"', tag)
        if name:
            values[name[1]] = html.unescape(value[1] if value else '')
    return values

def control(**kwargs):
    with urlopen(Request('http://127.0.0.1:58473/_control', json.dumps(kwargs).encode(), {'Content-Type': 'application/json'})) as response:
        assert response.status == 200

def login(name):
    client = build_opener(HTTPCookieProcessor(CookieJar()))
    _, document = fetch(client, '/Account/Login?' + urlencode({'returnUrl': origin + '#product-reviews'}))
    data = fields(document)
    data.update(UserName=name, Password='SyntheticReview123!', ReturnUrl=origin + '#product-reviews')
    destination, document = fetch(client, '/Account/Login', data)
    assert urlsplit(destination).path == '/Product/Details/SP-086', destination
    return client, document

control(review=None, reject=False, eligibility_error=False)
guest = build_opener(HTTPCookieProcessor(CookieJar()))
_, document = fetch(guest, origin)
assert 'id="product-reviews"' in document and 'id="review-body"' not in document
login_link = html.unescape(re.search(r'href="([^"]*/Account/Login\?returnUrl=[^"]*)"[^>]*>ĐĂNG NHẬP', document)[1])
assert parse_qs(urlsplit(login_link).query)['returnUrl'][0].endswith('#product-reviews')
results['guest_public_reviews_and_login_return'] = True

expired = build_opener(HTTPCookieProcessor(CookieJar()))
_, expired_page = fetch(expired, origin)
expired_data = fields(expired_page)
expired_data.update(productId='SP-086', orderId='HD-DELIVERED', stars='3', body='Giữ nội dung khi phiên đăng nhập hết hạn.', color='Đỏ', returnUrl='/Product?page=8')
login_url, login_page = fetch(expired, '/Customer/Review', expired_data)
assert urlsplit(login_url).path == '/Account/Login'
login_data = fields(login_page)
login_data.update(UserName='delivered', Password='SyntheticReview123!')
destination, restored_page = fetch(expired, '/Account/Login', login_data)
assert urlsplit(destination).path == '/Product/Details/SP-086' and urlsplit(destination).fragment == 'product-reviews'
assert expired_data['body'] in restored_page and 'id="review-body"' in restored_page
assert re.search(r'<option value="3" selected="selected">', restored_page)
results['expired_session_login_restores_product_and_draft'] = True

pending, document = login('pending')
Path(__file__).with_name('pending.html').write_text(document, encoding='utf-8')
assert 'Chưa có đơn đủ điều kiện' in document and 'id="review-body"' not in document
results['not_delivered_has_no_form'] = True

delivered, document = login('delivered')
assert 'id="review-body"' in document and 'HD-DELIVERED' in document
assert re.search(r'<option value="HD-DELIVERED" selected="selected">', document)
results['delivered_has_inline_form_and_selected_order'] = True

next_link = html.unescape(re.search(r'href="([^"]*)"[^>]*>Tiếp →', document)[1])
query = parse_qs(urlsplit(next_link).query)
assert query['color'] == ['Đỏ'] and query['returnUrl'] == ['/Product?page=8'] and query['reviewPage'] == ['2']
assert urlsplit(next_link).fragment == 'product-reviews'
_, second_page = fetch(delivered, next_link)
assert 'Trang 2' in second_page and 'id="detail-product-name"' in second_page
results['pagination_preserves_product_color_and_back_link'] = True

control(reject=True)
data = fields(document)
data.update(productId='SP-086', orderId='HD-DELIVERED', stars='4', body='Nội dung được giữ lại khi gửi lỗi.', color='Đỏ', returnUrl='/Product?page=8')
destination, rejected = fetch(delivered, '/Customer/Review', data)
assert urlsplit(destination).path == '/Product/Details/SP-086' and urlsplit(destination).fragment == 'product-reviews'
assert 'Đơn chưa đủ điều kiện' in rejected and 'Nội dung được giữ lại khi gửi lỗi.' in rejected
assert re.search(r'<option value="4" selected="selected">', rejected)
results['rejected_submission_keeps_body_stars_and_inline_error'] = True

control(reject=False)
data['__RequestVerificationToken'] = fields(rejected)['__RequestVerificationToken']
data['body'] = 'Đánh giá thử trên dữ liệu giả, hiển thị ngay dưới sản phẩm.'
destination, published = fetch(delivered, '/Customer/Review', data)
assert urlsplit(destination).path == '/Product/Details/SP-086' and urlsplit(destination).fragment == 'product-reviews'
assert 'Đã đăng đánh giá của bạn.' in published and data['body'] in published and 'Đánh giá của bạn' in published
assert 'id="review-body"' not in published
results['published_on_details_and_duplicate_form_removed'] = True

_, public_page = fetch(guest, origin)
assert data['body'] in public_page and 'yody-own-review' not in public_page
results['published_review_visible_to_guest'] = True

destination, _ = fetch(guest, '/Customer/Reviews/SP-086?' + urlencode({'page': 2, 'orderId': 'HD-DELIVERED'}))
assert urlsplit(destination).path == '/Product/Details/SP-086' and parse_qs(urlsplit(destination).query)['reviewPage'] == ['2']
assert urlsplit(destination).fragment == 'product-reviews'
results['legacy_review_link_redirects_to_details'] = True

control(review=None, eligibility_error=True)
_, failed = fetch(delivered, origin)
assert 'Chưa thể kiểm tra đơn hàng' in failed and 'id="detail-product-name"' in failed and 'id="review-body"' not in failed
results['eligibility_outage_keeps_product_and_blocks_form'] = True
control(review=None, eligibility_error=False)

Path(__file__).with_name('results.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(results, ensure_ascii=False, indent=2))
