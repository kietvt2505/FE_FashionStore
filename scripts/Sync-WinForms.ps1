param([switch]$StartBackend,[string]$OrderEmployeeId='NV-001',[int]$Port=5000)
$ErrorActionPreference='Stop'
$syncRoot=Split-Path -Parent $PSScriptRoot
& dotnet build (Join-Path $syncRoot 'tools/SupabaseSchemaSetup/SupabaseSchemaSetup.csproj') --verbosity minimal
if($LASTEXITCODE -ne 0){throw 'Chua build duoc bo dong bo.'}
$syncConnection=$null
$syncValue=$env:ConnectionStrings__Supabase
if([string]::IsNullOrWhiteSpace($syncValue)){$syncValue=$env:YODYSTORE_CONNECTION_STRING}
if([string]::IsNullOrWhiteSpace($syncValue)){$syncValue=[Environment]::GetEnvironmentVariable('ConnectionStrings__Supabase','User')}
if([string]::IsNullOrWhiteSpace($syncValue)){$syncValue=[Environment]::GetEnvironmentVariable('YODYSTORE_CONNECTION_STRING','User')}
if([string]::IsNullOrWhiteSpace($syncValue)){
    $syncConnection=Read-Host 'Nhap chuoi PostgreSQL Supabase (nhap kin, khong luu)' -AsSecureString
    $syncValue=[Net.NetworkCredential]::new('',$syncConnection).Password
}
$syncPrevious=$env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION
try {
    $env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION=$syncValue
    while($true){
        & dotnet (Join-Path $syncRoot 'tools/SupabaseSchemaSetup/bin/Debug/net8.0/SupabaseSchemaSetup.dll') --winforms `
            (Join-Path $syncRoot 'database/Supabase-DongBoWinForms.sql') (Join-Path $syncRoot 'evidence/winforms-sync-live-result.json')
        if($LASTEXITCODE -eq 0){break}
        Write-Host 'Giao dich da hoan tac. Da luu metadata trigger de kiem tra, khong co mat khau trong bao cao.'
        $syncRetry=Read-Host 'Sau khi sua ban dong bo, nhap R de thu lai; nhap Q de dong va xoa ket noi khoi phien'
        if($syncRetry -ne 'R'){throw 'Chua ap dung dong bo; giu backend hien tai.'}
    }
    if($StartBackend){
        $syncBackends=@(Get-Process -Name FashionStore.Backend -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -and ([IO.Path]::GetFullPath($_.Path)).StartsWith([IO.Path]::GetFullPath($syncRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
        })
        if($syncBackends.Count -gt 1){throw 'Co nhieu backend; can chon dung tien trinh truoc khi chay lai.'}
        if($syncBackends.Count -eq 1){Stop-Process -Id $syncBackends[0].Id;$syncBackends[0].WaitForExit()}
        $syncOldBackend=$env:ConnectionStrings__Supabase
        try {
            $env:ConnectionStrings__Supabase=$env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION
            & (Join-Path $PSScriptRoot 'Start-SupabaseCatalog.ps1') -Port $Port -EnableOrders -OrderEmployeeId $OrderEmployeeId -EnableBankTransfer
        }finally{$env:ConnectionStrings__Supabase=$syncOldBackend}
    }
}finally{$env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION=$syncPrevious;$syncValue=$null;if($syncConnection){$syncConnection.Dispose()}}
