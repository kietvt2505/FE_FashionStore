$ErrorActionPreference='Stop'
$root=(Get-Location).Path
$preview=Join-Path $root ('tmp\web-return-preview-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $preview | Out-Null
$preview | Set-Content 'tmp/web-return-preview-path.txt'
foreach($part in @('Content','Scripts','Views','bin','Global.asax','Web.config')){Copy-Item -LiteralPath (Join-Path $root ('FashionStore.Web\'+$part)) -Destination $preview -Recurse}
Copy-Item -LiteralPath 'tmp/web-return-mvc/FashionStore.Web.dll' -Destination (Join-Path $preview 'bin/FashionStore.Web.dll') -Force
[xml]$config=Get-Content (Join-Path $preview 'Web.config')
$config.configuration.appSettings.add | Where-Object {$_.key -eq 'FashionStoreApiBaseUrl'} | ForEach-Object {$_.value='http://127.0.0.1:5039'}
$config.configuration.'system.web'.authentication.forms.name='Yody.WebReturnPreview'
$config.Save((Join-Path $preview 'Web.config'))
[xml]$iis=Get-Content '.vs/FashionStore/config/applicationhost.config'
$site=$iis.configuration.'system.applicationHost'.sites.site | Where-Object {$_.name -eq 'FashionStore.Web'}
$site.application.virtualDirectory.physicalPath=$preview
$bindings=$site.SelectSingleNode('bindings');$bindings.RemoveAll()
$binding=$iis.CreateElement('binding');$binding.SetAttribute('protocol','http');$binding.SetAttribute('bindingInformation','*:58497:127.0.0.1');$null=$bindings.AppendChild($binding)
$iisPath=Join-Path $preview 'applicationhost.config';$iis.Save($iisPath)
$api=Start-Process -FilePath (Get-Command node).Source -ArgumentList @((Join-Path $root 'verification\customer-returns\mock-api.mjs')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $preview 'api.log') -RedirectStandardError (Join-Path $preview 'api-error.log')
$api.Id | Set-Content (Join-Path $preview 'api-pid.txt')
$web=Start-Process -FilePath 'C:\Program Files\IIS Express\iisexpress.exe' -ArgumentList @('/config:"'+$iisPath+'"','/site:FashionStore.Web','/systray:false') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $preview 'web.log') -RedirectStandardError (Join-Path $preview 'web-error.log')
$web.Id | Set-Content (Join-Path $preview 'web-pid.txt')
Write-Output 'Return preview: http://127.0.0.1:58497/Customer/Return?id=WEB-RETURN-PREVIEW'
