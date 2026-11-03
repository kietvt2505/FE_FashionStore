param([Parameter(Mandatory=$true)][string]$Names)
$ErrorActionPreference='Stop'
$loginRoot=Split-Path -Parent $PSScriptRoot
& dotnet build (Join-Path $loginRoot 'tools/SupabaseSchemaSetup/SupabaseSchemaSetup.csproj') --verbosity minimal
if($LASTEXITCODE -ne 0){throw 'Chua build duoc bo doc tai khoan.'}
$loginSecure=Read-Host 'Nhap chuoi PostgreSQL Supabase (nhap kin; chi doc trang thai tai khoan)' -AsSecureString
$loginPrevious=$env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION
$loginNames=@($Names.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
try {
    $env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION=[System.Net.NetworkCredential]::new('',$loginSecure).Password
    & dotnet (Join-Path $loginRoot 'tools/SupabaseSchemaSetup/bin/Debug/net8.0/SupabaseSchemaSetup.dll') --audit-logins `
        (Join-Path $loginRoot 'evidence/login-account-audit.json') @loginNames
    if($LASTEXITCODE -ne 0){throw 'Chua doc duoc trang thai tai khoan.'}
} finally {$env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION=$loginPrevious;$loginSecure.Dispose()}
