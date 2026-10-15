param([ValidateSet('Read','Apply','Remove','LegacyPause','LegacyResume')][string]$Action,[string]$Data='')
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
Import-Module (Join-Path $PSScriptRoot 'LegacyDns.psm1') -Force
$owner='AlterDPI.Managed.34B7F47A-2168-4DD3-9211-506FA4496890'
if($Action -eq 'Read'){
 $active=@(Get-NetIPInterface|Where-Object{[int]$_.ConnectionState -eq 1}|ForEach-Object{$_.InterfaceIndex})
 $r=@(Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue|Where-Object{$_.InterfaceIndex -in $active -and $_.State -ne 'Unreachable'}|Sort-Object @{e={$_.RouteMetric+$_.InterfaceMetric}})
 $family=4
 if(-not $r.Count){$r=@(Get-NetRoute -DestinationPrefix '::/0' -ErrorAction SilentlyContinue|Where-Object{$_.InterfaceIndex -in $active -and $_.NextHop -ne '::'}|Sort-Object @{e={$_.RouteMetric+$_.InterfaceMetric}});$family=6}
 if(-not $r.Count){exit 10};$route=$r[0]
 $a=Get-NetAdapter -InterfaceIndex $route.InterfaceIndex -ErrorAction SilentlyContinue
 $p=@(Get-NetConnectionProfile -InterfaceIndex $route.InterfaceIndex -ErrorAction SilentlyContinue)
 $i=Get-NetIPInterface -InterfaceIndex $route.InterfaceIndex -AddressFamily $(if($family -eq 4){'IPv4'}else{'IPv6'})|Select-Object -First 1
 $d=@(Get-DnsClientServerAddress -InterfaceIndex $route.InterfaceIndex|ForEach-Object{$_.ServerAddresses}|Where-Object{$_ -ne '127.0.0.1' -and $_ -ne '::1'})
 $suffix=@((Get-DnsClient -InterfaceIndex $route.InterfaceIndex).ConnectionSpecificSuffix;(Get-DnsClientGlobalSetting).SuffixSearchList)|Where-Object{$_}|ForEach-Object{$_.ToLowerInvariant().TrimEnd('.')}
 $foreign=@(Get-DnsClientNrptRule|Where-Object{$_.Comment -notin @($owner,'GoodbyeDPI / Furkan Okan','GoodbyeDPI encrypted DNS - managed local rule')}|ForEach-Object{($_.Namespace -join ',')+'='+($_.NameServers -join ',')}|Sort-Object); $user=[string](Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\AlterDPI' -Name OwnerSid -ErrorAction SilentlyContinue).OwnerSid; $proxy=if($user){Get-ItemProperty -LiteralPath ('Registry::HKEY_USERS\'+$user+'\Software\Microsoft\Windows\CurrentVersion\Internet Settings') -ErrorAction SilentlyContinue}; $proxyOn=$proxy -and ($proxy.ProxyEnable -eq 1 -or $proxy.AutoConfigURL)
 $name=if($p.Count){[string]$p[0].Name}else{''};$guid=if($a){[string]$a.InterfaceGuid}else{[string]$route.InterfaceIndex}
 $mtu=if($i.PSObject.Properties['NlMtu']){[int]$i.NlMtu}else{[int]$i.NlMtuBytes}
 [ordered]@{key=($guid+'|'+$route.NextHop+'|'+$name+'|'+($foreign -join ';')+'|'+[string]$proxy.ProxyServer+'|'+[string]$proxy.AutoConfigURL);adapter=[string]$route.InterfaceAlias;interfaceIndex=[int]$route.InterfaceIndex;family=$family;mtu=$mtu;hopLimit=[int][Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetIPv4GlobalStatistics().DefaultTtl;tunnel=(!$a -or !$a.HardwareInterface);mobile=($route.NextHop -like '172.20.10.*' -or $name -match 'iPhone|hotspot');managed=($p.Count -gt 0 -and $p[0].NetworkCategory -eq 'DomainAuthenticated');dns=$d;suffixes=@($suffix);hasIpv6=(@(Get-NetRoute -DestinationPrefix '::/0' -ErrorAction SilentlyContinue|Where-Object{$_.InterfaceIndex -in $active}).Count -gt 0);proxy=[bool]$proxyOn}|ConvertTo-Json -Compress -Depth 4
 exit 0
}
if($Action -in @('Apply','Remove')){
 $rules=@(Get-DnsClientNrptRule)
 if($Action -eq 'Remove'){
  foreach($r in $rules|Where-Object{$_.Comment -eq $owner -and $_.Namespace -contains '.' -and $_.NameServers -contains '127.0.0.1'}){Remove-DnsClientNrptRule -Name $r.Name -Force}
 }else{
  if(@($rules|Where-Object{$_.Namespace -contains '.' -and $_.Comment -ne $owner}).Count){exit 12}
  if(-not @($rules|Where-Object{$_.Comment -eq $owner}).Count){$null=Add-DnsClientNrptRule -Namespace '.' -NameServers '127.0.0.1' -Comment $owner -DisplayName 'AlterDPI'}
  ConvertTo-Json -InputObject @((Get-DnsClientNrptRule|Where-Object{$_.Comment -eq $owner}).Name) -Compress
 }
 Clear-DnsClientCache
 exit 0
}
# Migrate only the former package's marked installation and exact policy comments.
$legacy=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'GoodbyeDPI-FurkanOkan'
$marker=Join-Path $legacy 'install.owner'
if(!(Test-Path -LiteralPath $marker) -or [IO.File]::ReadAllText($marker) -ne 'GoodbyeDPI / Furkan Okan'){exit 0}
if((Get-Item -LiteralPath $legacy).Attributes -band [IO.FileAttributes]::ReparsePoint){exit 13}
$s=Get-CimInstance Win32_Service -Filter "Name='GoodbyeDPI'"
if($s -and $s.PathName -notlike ('"'+$legacy+'\*')){exit 13}
$t=Get-ScheduledTask -TaskName 'GoodbyeDPI-FurkanOkan' -ErrorAction SilentlyContinue
$ownTask=$t -and @($t.Actions|Where-Object{$_.Arguments -like ('*"'+$legacy+'\*')}).Count
$stateRoot=Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'AlterDPI'
if(!(Test-Path -LiteralPath (Join-Path $stateRoot 'state.owner')) -or [IO.File]::ReadAllText((Join-Path $stateRoot 'state.owner')) -ne $owner){exit 13}
$journal=Join-Path $stateRoot 'legacy-migration.json'
$comments=@('GoodbyeDPI / Furkan Okan','GoodbyeDPI encrypted DNS - managed local rule')
$domains=@('discord.com','discord.gg','discordapp.com','discordapp.net','discord.media','roblox.com','rbxcdn.com','rbx.com')
$owned=@(Get-DnsClientNrptRule|Where-Object{$_.Comment -in $comments -and @($_.Namespace|Where-Object{$_.TrimStart('.') -notin $domains}).Count -eq 0})
if($Action -eq 'LegacyPause'){
 if(!(Test-Path -LiteralPath $journal)){
  $snapshot=[ordered]@{taskEnabled=($ownTask -and $t.State -ne 'Disabled');taskRunning=($ownTask -and $t.State -eq 'Running');serviceRunning=($s -and $s.State -eq 'Running');serviceMode=if($s){$s.StartMode}else{''};rules=@($owned|ForEach-Object{[ordered]@{namespace=@($_.Namespace|ForEach-Object{[string]$_});nameServers=@($_.NameServers|ConvertTo-DnsServerText);comment=[string]$_.Comment}})}
  $temp=$journal+'.tmp';[IO.File]::WriteAllText($temp,($snapshot|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false));Move-Item -LiteralPath $temp -Destination $journal -Force
 }
 if($ownTask){Disable-ScheduledTask -TaskName $t.TaskName|Out-Null;Stop-ScheduledTask -TaskName $t.TaskName -ErrorAction SilentlyContinue}
 if($s){Stop-Service -Name GoodbyeDPI -ErrorAction SilentlyContinue;Set-Service -Name GoodbyeDPI -StartupType Manual}
 foreach($r in $owned){Remove-DnsClientNrptRule -Name $r.Name -Force}
}else{
 if(!(Test-Path -LiteralPath $journal)){exit 0}
 $before=Get-Content -LiteralPath $journal -Raw|ConvertFrom-Json
 foreach($r in $before.rules){if($r.comment -notin $comments -or @($r.namespace|Where-Object{$_.TrimStart('.') -notin $domains}).Count){exit 13};$servers=@($r.nameServers|ConvertTo-DnsServerText);if(-not $servers.Count){exit 13};if(-not @($owned|Where-Object{$_.Comment -eq $r.comment -and ($_.Namespace -join ',') -eq ($r.namespace -join ',')}).Count){Add-DnsClientNrptRule -Namespace $r.namespace -NameServers $servers -Comment $r.comment -DisplayName $r.comment|Out-Null}}
 if($s){$mode=switch($before.serviceMode){'Auto'{'Automatic'} 'Disabled'{'Disabled'} default{'Manual'}};Set-Service -Name GoodbyeDPI -StartupType $mode;if($before.serviceRunning){Start-Service -Name GoodbyeDPI}}
 if($ownTask -and $before.taskEnabled){Enable-ScheduledTask -TaskName $t.TaskName|Out-Null;if($before.taskRunning){Start-ScheduledTask -TaskName $t.TaskName}}
 Remove-Item -LiteralPath $journal
}
Clear-DnsClientCache
