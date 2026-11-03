param([switch]$StartBackend,[string]$OrderEmployeeId='NV-001',[int]$Port=5000)
$ErrorActionPreference='Stop'
$columnRoot=Split-Path -Parent $PSScriptRoot
& dotnet build (Join-Path $columnRoot 'tools/SupabaseSchemaSetup/SupabaseSchemaSetup.csproj') --verbosity minimal
if($LASTEXITCODE -ne 0){throw 'Chua build duoc bo cap nhat cot.'}
$columnSecure=Read-Host 'Nhap chuoi PostgreSQL Supabase hien tai (nhap kin, khong luu)' -AsSecureString
$columnPrevious=$env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION
try {
    $env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION=[System.Net.NetworkCredential]::new('',$columnSecure).Password
    & dotnet (Join-Path $columnRoot 'tools/SupabaseSchemaSetup/bin/Debug/net8.0/SupabaseSchemaSetup.dll') --columns `
        (Join-Path $columnRoot 'database/Supabase-BoSungCot-ChucNang.sql') `
        (Join-Path $columnRoot 'database/schema-reference/Supabase_public_metadata_20261103.json') `
        (Join-Path $columnRoot 'evidence/native18-columns-live-result.json')
    if($LASTEXITCODE -ne 0){throw 'Chua hoan tat cap nhat. Khong khoi dong source moi.'}
    if($StartBackend){
        $columnBackend=@(Get-Process -Name FashionStore.Backend -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -and ([IO.Path]::GetFullPath($_.Path)).StartsWith([IO.Path]::GetFullPath($columnRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
        })
        if($columnBackend.Count -gt 1){throw 'Co nhieu backend dang chay. Can chon dung backend truoc khi khoi dong lai.'}
        if($columnBackend.Count -eq 1){Stop-Process -Id $columnBackend[0].Id; $columnBackend[0].WaitForExit()}
        $columnPreviousBackend=$env:ConnectionStrings__Supabase
        try {
            $env:ConnectionStrings__Supabase=$env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION
            & (Join-Path $PSScriptRoot 'Start-SupabaseCatalog.ps1') -Port $Port -EnableOrders -OrderEmployeeId $OrderEmployeeId -EnableBankTransfer
        } finally {$env:ConnectionStrings__Supabase=$columnPreviousBackend}
    }
} finally {
    $env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION=$columnPrevious
    $columnSecure.Dispose()
}
