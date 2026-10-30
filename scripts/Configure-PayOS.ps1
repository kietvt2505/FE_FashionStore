param([switch]$RotateKeys,[string]$PublicApiBaseUrl,[string]$StorefrontBaseUrl)
$ErrorActionPreference='Stop'
$taskSecretsDirectory=Join-Path $env:APPDATA 'Microsoft\UserSecrets\FashionStore-Backend-Development'
$taskSecretsPath=Join-Path $taskSecretsDirectory 'secrets.json'
$taskSettings=@{}
if(Test-Path -LiteralPath $taskSecretsPath){
    $taskObject=Get-Content -LiteralPath $taskSecretsPath -Raw | ConvertFrom-Json
    foreach($taskProperty in $taskObject.PSObject.Properties){$taskSettings[$taskProperty.Name]=$taskProperty.Value}
}
function Read-Key([string]$Name,[string]$EnvironmentName,[string]$Label){
    if(-not $RotateKeys -and $taskSettings[$Name]){return}
    $taskInput=[Environment]::GetEnvironmentVariable($EnvironmentName,'Process')
    if(-not $taskInput){
        $taskSecure=Read-Host $Label -AsSecureString
        $taskPointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($taskSecure)
        try{$taskInput=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($taskPointer)}
        finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($taskPointer);$taskSecure.Dispose()}
    }
    if([string]::IsNullOrWhiteSpace($taskInput)){throw 'Missing payOS key.'}
    $taskSettings[$Name]=$taskInput.Trim()
}
Read-Key 'Payments:PayOS:ClientId' 'PAYOS_CLIENT_ID' 'payOS Client ID (hidden)'
Read-Key 'Payments:PayOS:ApiKey' 'PAYOS_API_KEY' 'payOS API key (hidden)'
Read-Key 'Payments:PayOS:ChecksumKey' 'PAYOS_CHECKSUM_KEY' 'payOS checksum key (hidden)'
$taskSettings['Payments:PayOS:Enabled']='true'
$taskSettings['Payments:PayOS:BankBin']='970422'
$taskSettings['Payments:PayOS:AccountNumber']='0924020138'
if($PublicApiBaseUrl -or $StorefrontBaseUrl){
    $taskHostNames=@('localhost','127.0.0.1')
    foreach($taskUrl in @($PublicApiBaseUrl,$StorefrontBaseUrl)){
        $taskUri=$null
        if(-not [Uri]::TryCreate($taskUrl,[UriKind]::Absolute,[ref]$taskUri) -or $taskUri.Scheme -ne 'https' -or $taskUri.IsLoopback -or $taskUri.Query -or $taskUri.Fragment -or $taskUri.UserInfo){throw 'Both public HTTPS base URLs are required.'}
        $taskHostNames+=$taskUri.Host
    }
    $taskSettings['Payments:PublicApiBaseUrl']=$PublicApiBaseUrl.TrimEnd('/')
    $taskSettings['Payments:StorefrontBaseUrl']=$StorefrontBaseUrl.TrimEnd('/')
    $taskSettings['Payments:PayOS:LocalDevelopment']='false'
    $taskSettings['AllowedHosts']=(($taskSettings['AllowedHosts'] -split ';')+$taskHostNames | Where-Object {$_} | Select-Object -Unique) -join ';'
}else{$taskSettings['Payments:PayOS:LocalDevelopment']='true'}
[void][IO.Directory]::CreateDirectory($taskSecretsDirectory)
[IO.File]::WriteAllText($taskSecretsPath,($taskSettings | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
$taskLocalPath=Join-Path $PSScriptRoot '..\FashionStore.Backend\appsettings.Development.json'
$taskLocalConfig=if(Test-Path -LiteralPath $taskLocalPath){Get-Content -LiteralPath $taskLocalPath -Raw | ConvertFrom-Json}else{[pscustomobject]@{}}
if($null -eq $taskLocalConfig.Payments){$taskLocalConfig | Add-Member -NotePropertyName Payments -NotePropertyValue ([pscustomobject]@{})}
$taskLocalPayos=[ordered]@{Enabled=$true;LocalDevelopment=$taskSettings['Payments:PayOS:LocalDevelopment'];
    ClientId=$taskSettings['Payments:PayOS:ClientId'];ApiKey=$taskSettings['Payments:PayOS:ApiKey'];ChecksumKey=$taskSettings['Payments:PayOS:ChecksumKey'];
    BankBin=$taskSettings['Payments:PayOS:BankBin'];AccountNumber=$taskSettings['Payments:PayOS:AccountNumber']}
$taskLocalConfig.Payments | Add-Member -NotePropertyName PayOS -NotePropertyValue $taskLocalPayos -Force
if($PublicApiBaseUrl){
    $taskLocalConfig.Payments | Add-Member -NotePropertyName PublicApiBaseUrl -NotePropertyValue $taskSettings['Payments:PublicApiBaseUrl'] -Force
    $taskLocalConfig.Payments | Add-Member -NotePropertyName StorefrontBaseUrl -NotePropertyValue $taskSettings['Payments:StorefrontBaseUrl'] -Force
    $taskLocalConfig | Add-Member -NotePropertyName AllowedHosts -NotePropertyValue $taskSettings['AllowedHosts'] -Force
}
[IO.File]::WriteAllText($taskLocalPath,($taskLocalConfig | ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
Write-Host 'payOS configuration saved in .NET User Secrets and backend-only appsettings.Development.json (ignored by Git).'
Write-Host 'Restart the backend in Development mode. Local mode uses manual payment result checks until public HTTPS/webhook is configured.'
