param([string]$ResultPath,[switch]$StartBackend,[switch]$EnableBankTransfer,[string]$OrderEmployeeId='NV-001',[int]$Port=5000)
$ErrorActionPreference='Stop'
$schemaRoot=Split-Path -Parent $PSScriptRoot
if([string]::IsNullOrWhiteSpace($ResultPath)){$ResultPath=Join-Path $schemaRoot 'evidence/vietnamese-schema-live-result.json'}
$setupProject=Join-Path $schemaRoot 'tools/SupabaseSchemaSetup/SupabaseSchemaSetup.csproj'
& dotnet build $setupProject --verbosity minimal
if($LASTEXITCODE -ne 0){throw 'Chua build duoc bo cai.'}
$schemaSecure=Read-Host 'Nhap chuoi ket noi PostgreSQL Supabase hien tai (nhap kin, khong luu)' -AsSecureString
$schemaPrevious=$env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION
try {
    $env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION=[System.Net.NetworkCredential]::new('',$schemaSecure).Password
    & dotnet (Join-Path $schemaRoot 'tools/SupabaseSchemaSetup/bin/Debug/net8.0/SupabaseSchemaSetup.dll') `
        (Join-Path $schemaRoot 'database/Supabase-Website-TenViet.sql') `
        (Join-Path $schemaRoot 'database/schema-reference/Supabase_public_metadata_20261103.json') $ResultPath
    if($LASTEXITCODE -ne 0){throw 'Chua ap dung bo cai. Xem thong bao phia tren.'}
    if($StartBackend){
        $schemaPreviousBackendConnection=$env:ConnectionStrings__Supabase
        try {
            $env:ConnectionStrings__Supabase=$env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION
            & (Join-Path $PSScriptRoot 'Start-SupabaseCatalog.ps1') -Port $Port -EnableOrders -OrderEmployeeId $OrderEmployeeId -EnableBankTransfer:$EnableBankTransfer
        } finally {$env:ConnectionStrings__Supabase=$schemaPreviousBackendConnection}
    }
} finally {
    $env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION=$schemaPrevious
    $schemaSecure.Dispose()
}
Read-Host 'Da xong. Nhan Enter de dong cua so'
