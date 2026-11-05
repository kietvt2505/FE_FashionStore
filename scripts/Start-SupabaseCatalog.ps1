param([int]$Port = 5000, [switch]$EnableCustomerFeatures, [switch]$EnableOrders, [string]$OrderEmployeeId = "NV-001", [switch]$EnableBankTransfer)
$ErrorActionPreference = 'Stop'
if ($EnableBankTransfer -and -not $EnableOrders) { throw '-EnableBankTransfer requires -EnableOrders.' }
$projectPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\FashionStore.Backend\FashionStore.Backend.csproj'))
function Stop-ExistingProjectBackend {
    $outputDirectory = Join-Path (Split-Path -Parent $projectPath) 'bin\Debug\net8.0'
    $backendExe = Join-Path $outputDirectory 'FashionStore.Backend.exe'
    $backendDll = Join-Path $outputDirectory 'FashionStore.Backend.dll'
    $dllArgument = '(?i)(?:^|[\s"])' + [regex]::Escape($backendDll) + '(?:$|[\s"])'
    $candidates = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='FashionStore.Backend.exe'"
    foreach ($candidate in $candidates) {
        $ownsOutput = $candidate.ExecutablePath -eq $backendExe
        if ($candidate.Name -eq 'dotnet.exe' -and $candidate.CommandLine) {
            $ownsOutput = $candidate.CommandLine.Replace('/', '\') -match $dllArgument
        }
        if (-not $ownsOutput) { continue }
        $backendProcess = Get-Process -Id $candidate.ProcessId -ErrorAction SilentlyContinue
        if (-not $backendProcess) { continue }
        if ([Math]::Abs(($backendProcess.StartTime - $candidate.CreationDate).TotalSeconds) -gt 1) {
            throw 'Backend process changed while checking ownership. Run the command again.'
        }
        Write-Host "Stopping this project's previous backend (PID $($candidate.ProcessId)) before rebuilding."
        Stop-Process -InputObject $backendProcess -ErrorAction Stop
        if (-not $backendProcess.WaitForExit(10000)) { throw 'Previous backend did not stop; build was not started.' }
    }
}
$previousProvider = $env:Database__Provider
$previousConnection = $env:ConnectionStrings__Supabase
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousBankTransfer = $env:Orders__EnableBankTransfer
$previousOrders = $env:Database__EnableSupabaseOrders
$previousEmployee = $env:Orders__EmployeeId
$previousCustomerFeatures = $env:Database__EnableSupabaseCustomerFeatures
try {
    $connection = $env:ConnectionStrings__Supabase
    if ([string]::IsNullOrWhiteSpace($connection)) { $connection = $env:YODYSTORE_CONNECTION_STRING }
    if ([string]::IsNullOrWhiteSpace($connection)) {
        $connection = [Environment]::GetEnvironmentVariable('ConnectionStrings__Supabase', 'User')
    }
    if ([string]::IsNullOrWhiteSpace($connection)) {
        $connection = [Environment]::GetEnvironmentVariable('YODYSTORE_CONNECTION_STRING', 'User')
    }
    if ([string]::IsNullOrWhiteSpace($connection)) {
        $secureConnection = Read-Host 'Paste the PostgreSQL connection string (hidden; not saved)' -AsSecureString
        $connection = [System.Net.NetworkCredential]::new('', $secureConnection).Password
        $secureConnection.Dispose()
    }
    if ([string]::IsNullOrWhiteSpace($connection)) { throw 'A PostgreSQL connection string is required.' }
    Stop-ExistingProjectBackend
    $env:Database__Provider = 'Supabase'
    $env:ConnectionStrings__Supabase = $connection
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Database__EnableSupabaseCustomerFeatures = ($EnableCustomerFeatures.IsPresent -or $EnableOrders.IsPresent).ToString()
    $env:Database__EnableSupabaseOrders = $EnableOrders.IsPresent.ToString()
    $env:Orders__EmployeeId = $OrderEmployeeId
    $env:Orders__EnableBankTransfer = $EnableBankTransfer.IsPresent.ToString()
    Write-Host "Starting Supabase backend at http://127.0.0.1:$Port"
    Write-Host 'Uses the 18 existing partner tables with approved Vietnamese columns. Startup does not modify the schema.'
    if ($EnableCustomerFeatures -or $EnableOrders) {
        Write-Host 'Customer accounts and inline reviews enabled; carts and favorites stay in the browser. Apply database/Supabase-BoSungCot-ChucNang.sql once.'
    } else { Write-Host 'Read-only catalog mode. Customer accounts and carts are disabled.' }
    if ($EnableOrders) { Write-Host "Orders enabled for employee $OrderEmployeeId. Orders use hoadon and chitiethoadon directly." }
    else { Write-Host 'Checkout is disabled.' }
    if ($EnableBankTransfer) { Write-Host 'Bank transfers enabled; payments require staff verification.' }
    if ($EnableCustomerFeatures -or $EnableOrders) { Write-Host 'Email recovery also requires SMTP and an HTTPS reset URL.' }
    Write-Host 'Admin endpoints are not yet migrated.'
    dotnet run --project $projectPath --no-launch-profile --urls "http://127.0.0.1:$Port"
    if ($LASTEXITCODE -ne 0) { throw 'Backend failed. Check the build/startup output above.' }
}
finally {
    $env:Database__Provider = $previousProvider
    $env:ConnectionStrings__Supabase = $previousConnection
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:Database__EnableSupabaseCustomerFeatures = $previousCustomerFeatures
    $env:Database__EnableSupabaseOrders = $previousOrders
    $env:Orders__EmployeeId = $previousEmployee
    $env:Orders__EnableBankTransfer = $previousBankTransfer
    $connection = $null
}
