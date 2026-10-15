param([string]$Dotnet='dotnet',[string]$MakeNsis='makensis',[string]$Keys='', [switch]$UnsignedCi)
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 $env:ALTERDPI_BUILD=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
 foreach($name in @('payload','release')){
  $target=[IO.Path]::GetFullPath((Join-Path $root ('artifacts\'+$name)))
  if(!$target.StartsWith(([IO.Path]::GetFullPath($root)+'\artifacts\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid output path'}
  if(Test-Path -LiteralPath $target){if((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Invalid output link'};Remove-Item -LiteralPath $target -Recurse -Force}
 }
 New-Item -ItemType Directory -Path 'artifacts\payload','artifacts\release' -Force|Out-Null
 & $Dotnet publish 'src\AlterDPI.App' -c Release -r win-x64 --self-contained true -o 'artifacts\payload' --nologo
 if($LASTEXITCODE){throw 'publish failed'}
 foreach($kind in @('chromium','firefox')){Copy-Item -Path 'browser\common\*' -Destination ('browser\'+$kind) -Force}
 Copy-Item -LiteralPath 'artifacts\payload\AlterDPI.exe' -Destination 'artifacts\payload\AlterDPI.Native.exe'
 Copy-Item -LiteralPath 'browser' -Destination 'artifacts\payload' -Recurse -Force
 Copy-Item -LiteralPath 'assets\browser.json' -Destination 'artifacts\payload\browser\browser.json'
 Copy-Item -LiteralPath 'third_party\licenses' -Destination 'artifacts\payload\legal' -Recurse -Force
 Copy-Item -LiteralPath 'third_party\NOTICES.md','LICENSE' -Destination 'artifacts\payload\legal'
 if($UnsignedCi){return}
 $signingKey=if($Keys){Join-Path $Keys 'Application.dpapi'}else{'ci-signing-key'}
 & $Dotnet run --project 'src\AlterDPI.Packager' -- sign 'artifacts\payload' application $signingKey
 if($LASTEXITCODE){throw 'payload signature failed'}
 & $MakeNsis /V2 'packaging\installer.nsi'
 if($LASTEXITCODE){throw 'installer failed'}
 & $Dotnet run --project 'src\AlterDPI.Packager' -- update 'artifacts\release' application $signingKey
 if($LASTEXITCODE){throw 'release signature failed'}
}finally{Pop-Location}
