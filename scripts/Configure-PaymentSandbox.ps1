param([switch]$VNPay,[switch]$ZaloPay,[switch]$Visa)
$ErrorActionPreference='Stop'
if(-not $VNPay -and -not $ZaloPay){throw 'Choose -VNPay and/or -ZaloPay.'}
if($Visa -and -not $VNPay){throw '-Visa requires -VNPay and international card acceptance from the merchant provider.'}
function Read-PublicUrl([string]$Label){
    $taskValue=(Read-Host $Label).Trim().TrimEnd('/')
    $taskUri=$null
    if(-not [Uri]::TryCreate($taskValue,[UriKind]::Absolute,[ref]$taskUri) -or $taskUri.Scheme -ne 'https' -or $taskUri.IsLoopback -or $taskUri.UserInfo -or $taskUri.Query -or $taskUri.Fragment){throw 'An HTTPS public base URL without query, credentials or fragment is required.'}
    return $taskValue
}
function Set-Secret([string]$Name,[string]$Label){
    $taskSecret=Read-Host $Label -AsSecureString
    $taskPointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($taskSecret)
    try{[Environment]::SetEnvironmentVariable($Name,[Runtime.InteropServices.Marshal]::PtrToStringBSTR($taskPointer),'Process')}
    finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($taskPointer);$taskSecret.Dispose()}
}
$env:Payments__Sandbox='true'
$env:Payments__PublicApiBaseUrl=Read-PublicUrl 'Public HTTPS API base URL'
$env:Payments__StorefrontBaseUrl=Read-PublicUrl 'Public HTTPS website base URL'
$env:Payments__VNPay__Enabled=$VNPay.IsPresent.ToString()
$env:Payments__VNPay__EnableInternationalCards=$Visa.IsPresent.ToString()
$env:Payments__ZaloPay__Enabled=$ZaloPay.IsPresent.ToString()
if($VNPay){
    $env:Payments__VNPay__TmnCode=(Read-Host 'VNPay TmnCode').Trim()
    Set-Secret 'Payments__VNPay__HashSecret' 'VNPay HashSecret (hidden)'
}
if($ZaloPay){
    $env:Payments__ZaloPay__AppId=(Read-Host 'ZaloPay AppId').Trim()
    Set-Secret 'Payments__ZaloPay__Key1' 'ZaloPay Key1 (hidden)'
    Set-Secret 'Payments__ZaloPay__Key2' 'ZaloPay Key2 (hidden)'
}
Write-Host 'Sandbox configuration is set for this PowerShell window only. Start the backend from this same window.'
