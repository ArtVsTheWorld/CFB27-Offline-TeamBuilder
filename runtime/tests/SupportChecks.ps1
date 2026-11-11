#requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$Collector)
$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('otb-support-check-'+[Guid]::NewGuid().ToString('N'))
$data=Join-Path $root 'UserData';$output=Join-Path $root 'Support';$manager=Join-Path $root 'Program Files\MMC'
New-Item -ItemType Directory -Path $data,$manager,(Join-Path $data 'NativeLogs') -Force|Out-Null
foreach($name in @('runtime.log','status.json','status.txt','environment.json','game-context.json')){[IO.File]::WriteAllText((Join-Path $data $name),'private fixture')}
foreach($name in @('OfflineTeamBuilderRuntime.settings.json','OfflineTeamBuilderRuntime.repair.json','private-save','OfflineTeamBuilderNative.dll')){[IO.File]::WriteAllText((Join-Path $data $name),'must not be shared')}
foreach($number in @(1,2,3)){[IO.File]::WriteAllText((Join-Path $data ('NativeLogs\'+$number+'-12345.log')),('x'*300KB))}
& $Collector -DiagnosticsRoot $data -OutputDirectory $output -MmcRoot $manager
$zip=@(Get-ChildItem -LiteralPath $output -Filter '*.zip')[0].FullName
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
    $names=@($archive.Entries|ForEach-Object FullName)
    if(@(Compare-Object (@('runtime.log','status.json','status.txt','environment.json','game-context.json','support.json')|Sort-Object) (@($names|Where-Object {$_ -notlike 'native-*'})|Sort-Object)).Count){throw 'Support archive root allow-list differs.'}
    $tails=@($archive.Entries|Where-Object FullName -like 'native-*.tail.txt')
    if($tails.Count -ne 2 -or @($tails|Where-Object Length -gt 256KB).Count){throw 'Native log tails are not bounded.'}
    if(@($names|Where-Object {$_ -match '\.dll$|repair|settings|save|blob'}).Count){throw 'Private files entered support archive.'}
}finally{$archive.Dispose()}
$blocked=$false
try{& $Collector -DiagnosticsRoot $data -OutputDirectory (Join-Path $manager 'Plugins\Support') -MmcRoot $manager}catch{$blocked=$true}
if(-not $blocked -or (Test-Path -LiteralPath (Join-Path $manager 'Plugins\Support'))){throw 'Collector allowed installation-folder output.'}
Write-Output 'PASS: support collector allow-list, bounded native tails, private-file exclusion and installation-folder write refusal.'
