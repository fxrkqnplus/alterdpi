function ConvertTo-DnsServerText {
 [CmdletBinding()]
 param([Parameter(Mandatory,ValueFromPipeline)]$Value)
 process {
  [Net.IPAddress]$address=$null
  if($Value -is [Net.IPAddress]){$address=$Value}
  elseif($Value -is [string]){if(-not [Net.IPAddress]::TryParse($Value,[ref]$address)){throw 'LEGACY_DNS'}}
  elseif($Value.PSObject.Properties['IPAddressToString'] -and [Net.IPAddress]::TryParse([string]$Value.IPAddressToString,[ref]$address)){}
  elseif([int]$Value.AddressFamily -eq 2 -and $null -ne $Value.Address -and [long]$Value.Address -ge 0 -and [long]$Value.Address -le [uint32]::MaxValue){$address=[Net.IPAddress]::new([long]$Value.Address)}
  else{throw 'LEGACY_DNS'}
  if([Net.IPAddress]::IsLoopback($address) -or $address.Equals([Net.IPAddress]::Any) -or $address.Equals([Net.IPAddress]::IPv6Any)){throw 'LEGACY_DNS'}
  $address.ToString()
 }
}
Export-ModuleMember -Function ConvertTo-DnsServerText
