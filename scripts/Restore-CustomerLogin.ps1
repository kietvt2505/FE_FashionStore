param([Parameter(Mandatory=$true)][string]$CustomerId,[Parameter(Mandatory=$true)][string]$LoginName,
    [Parameter(Mandatory=$true)][string]$ExpectedEmail,[Parameter(Mandatory=$true)][string]$ExpectedPhone)
$ErrorActionPreference='Stop'
$restoreRoot=Split-Path -Parent $PSScriptRoot
& dotnet build (Join-Path $restoreRoot 'tools/SupabaseSchemaSetup/SupabaseSchemaSetup.csproj') --verbosity minimal
if($LASTEXITCODE -ne 0){throw 'Chua build duoc bo khoi phuc.'}
$restoreConnection=Read-Host 'Nhap chuoi PostgreSQL Supabase (nhap kin)' -AsSecureString
$restorePassword=$null;$restoreConfirm=$null;$restorePlain=$null;$restoreConfirmPlain=$null
$restoreOldConnection=$env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION;$restoreOldPassword=$env:FASHIONSTORE_LOGIN_PASSWORD
try {
    do {
        if($restorePassword){$restorePassword.Dispose()};if($restoreConfirm){$restoreConfirm.Dispose()}
        $restorePassword=Read-Host "Nhap mat khau MOI cho $LoginName (12-128 ky tu, co chu va so)" -AsSecureString
        $restoreConfirm=Read-Host 'Nhap lai mat khau moi' -AsSecureString
        $restorePlain=[System.Net.NetworkCredential]::new('',$restorePassword).Password
        $restoreConfirmPlain=[System.Net.NetworkCredential]::new('',$restoreConfirm).Password
        $restoreValid=$restorePlain.Length -ge 12 -and $restorePlain.Length -le 128 -and $restorePlain -cmatch '[A-Za-z]' -and $restorePlain -match '[0-9]' -and $restorePlain -ceq $restoreConfirmPlain
        if(-not $restoreValid){Write-Host 'Chua hop le hoac chua trung khop. Vui long nhap lai.'}
    } while(-not $restoreValid)
    $env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION=[System.Net.NetworkCredential]::new('',$restoreConnection).Password
    $env:FASHIONSTORE_LOGIN_PASSWORD=$restorePlain
    $restoreReport=Join-Path $restoreRoot 'evidence/restored-customer-login.json'
    & dotnet (Join-Path $restoreRoot 'tools/SupabaseSchemaSetup/bin/Debug/net8.0/SupabaseSchemaSetup.dll') --restore-login `
        $restoreReport $CustomerId $LoginName $ExpectedEmail $ExpectedPhone
    if($LASTEXITCODE -ne 0){throw 'Chua khoi phuc; xem thong bao phia tren.'}
    $env:FASHIONSTORE_LOGIN_PASSWORD=$restoreOldPassword
    $restoreResult=Get-Content $restoreReport -Raw|ConvertFrom-Json
    $restoreWebStatus=0;$restoreWebVerified=$false
    try {
        $restoreSession=Invoke-RestMethod 'http://127.0.0.1:5000/api/account/login' -Method Post -ContentType 'application/json' `
            -Body (@{UserName=$LoginName;Password=$restorePlain}|ConvertTo-Json -Compress) -TimeoutSec 20
        $restoreHeader=@{Authorization='Bearer '+$restoreSession.accessToken}
        try {
            $restoreProfile=Invoke-RestMethod 'http://127.0.0.1:5000/api/account/me' -Headers $restoreHeader -TimeoutSec 15
            $restoreWebStatus=200;$restoreWebVerified=$restoreProfile.id -eq $CustomerId
        } finally {Invoke-RestMethod 'http://127.0.0.1:5000/api/account/logout' -Method Post -Headers $restoreHeader -TimeoutSec 10|Out-Null}
    } catch {if($_.Exception.Response){$restoreWebStatus=[int]$_.Exception.Response.StatusCode};Write-Host 'Da luu dang nhap, nhung chua xac nhan duoc API. Khong can dat lai mat khau.'}
    $restoreResult|Add-Member -NotePropertyName BackendStatus -NotePropertyValue $restoreWebStatus
    $restoreResult|Add-Member -NotePropertyName BackendCustomerConfirmed -NotePropertyValue $restoreWebVerified
    $restoreResult|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $restoreReport -Encoding utf8
    if($restoreWebVerified){Write-Host 'DA KIEM TRA: dang nhap website thanh cong, dung ho so khach hang.'}
} finally {
    $env:FASHIONSTORE_SCHEMA_SETUP_CONNECTION=$restoreOldConnection;$env:FASHIONSTORE_LOGIN_PASSWORD=$restoreOldPassword
    $restorePlain=$null;$restoreConfirmPlain=$null;$restoreConnection.Dispose()
    if($restorePassword){$restorePassword.Dispose()};if($restoreConfirm){$restoreConfirm.Dispose()}
}
