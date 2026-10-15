$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '..\packaging\LegacyDns.psm1') -Force
$passed=0
function Assert-Equal($actual,$expected){if($actual -ne $expected){throw 'Unexpected address'};$script:passed++}
Assert-Equal ('8.8.8.8'|ConvertTo-DnsServerText) '8.8.8.8'
Assert-Equal ([Net.IPAddress]::Parse('1.1.1.1')|ConvertTo-DnsServerText) '1.1.1.1'
$old=[Net.IPAddress]::Parse('1.0.0.1')|ConvertTo-Json -Depth 4|ConvertFrom-Json
Assert-Equal ($old|ConvertTo-DnsServerText) '1.0.0.1'
Assert-Equal ([Net.IPAddress]::Parse('2606:4700:4700::1111')|ConvertTo-DnsServerText) '2606:4700:4700::1111'
$snapshot=@([Net.IPAddress]::Parse('8.8.8.8'),[Net.IPAddress]::Parse('2001:4860:4860::8888'))|ConvertTo-DnsServerText|ConvertTo-Json|ConvertFrom-Json
Assert-Equal $snapshot[0] '8.8.8.8'
Assert-Equal $snapshot[1] '2001:4860:4860::8888'
foreach($bad in @('127.0.0.1','::1','0.0.0.0','::','not-an-ip',[pscustomobject]@{AddressFamily=23;Address=$null})){
 $rejected=$false;try{$bad|ConvertTo-DnsServerText|Out-Null}catch{$rejected=$true};if(!$rejected){throw 'Unsafe address accepted'};$passed++
}
@{passed=$passed;failed=0}|ConvertTo-Json -Compress
