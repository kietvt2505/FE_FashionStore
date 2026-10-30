param(
    [string]$ApiBaseUrl = 'http://127.0.0.1:5000',
    [string]$OutputPath,
    [switch]$Open
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $repoRoot 'tmp\catalog-classification.html' }
$baseUri = $null
if (-not [Uri]::TryCreate($ApiBaseUrl, [UriKind]::Absolute, [ref]$baseUri) -or
    $baseUri.Scheme -notin @('http','https') -or $baseUri.Query -or $baseUri.Fragment -or $baseUri.UserInfo) {
    throw 'ApiBaseUrl phải là địa chỉ HTTP/HTTPS của backend, không kèm mật khẩu hoặc tham số.'
}
$apiRoot = $ApiBaseUrl.TrimEnd('/')
$products = @{}
$page = 1
do {
    try { $result = Invoke-RestMethod -Uri "$apiRoot/api/products?sort=name&page=$page&pageSize=100" -TimeoutSec 30 }
    catch { throw 'Chưa đọc được backend. Hãy chạy .\scripts\Start-SupabaseCatalog.ps1 ở cửa sổ PowerShell khác, rồi chạy lại lệnh này. Không dán mật khẩu vào file HTML.' }
    if ($null -eq $result.items -or $null -eq $result.total -or $result.page -ne $page) { throw 'Backend trả dữ liệu sản phẩm không đúng định dạng.' }
    foreach ($item in $result.items) {
        if ([string]::IsNullOrWhiteSpace($item.id)) { throw 'Có sản phẩm thiếu mã; dừng xuất để tránh phân loại sai.' }
        $products[$item.id] = [ordered]@{
            id = [string]$item.id; name = [string]$item.name; category = [string]$item.category.name
            audience = [string]$item.audience; kind = [string]$item.kind; image = [string]$item.imageUrl
        }
    }
    $page++
    if ($page -gt 1000) { throw 'Danh sách vượt giới hạn xuất; hãy kiểm tra lại backend.' }
} while ($result.items.Count -gt 0 -and ($page - 1) * 100 -lt $result.total)
if ($products.Count -eq 0) { throw 'Backend chưa có sản phẩm hiển thị. Kiểm tra database và trạng thái sản phẩm trước.' }
$data = [ordered]@{ source = $apiRoot; products = @($products.Values | Sort-Object { $_.id }) }
$json = ($data | ConvertTo-Json -Depth 6 -Compress).Replace('<','\u003c').Replace('>','\u003e').Replace('&','\u0026')
$template = [IO.File]::ReadAllText((Join-Path $repoRoot 'tools\catalog-classification.template.html'))
$destination = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllText($destination, $template.Replace('__CATALOG_JSON__',$json), [Text.UTF8Encoding]::new($false))
Write-Host "Đã xuất $($products.Count) sản phẩm vào: $destination"
Write-Host 'Mở file HTML, chọn đối tượng và loại cho từng sản phẩm, rồi tải SQL. Công cụ không tự ghi vào Supabase.'
if ($Open) { Invoke-Item -LiteralPath $destination }
