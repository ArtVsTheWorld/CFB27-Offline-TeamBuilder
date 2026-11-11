param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'bin\Release'))
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force|Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $vs){throw 'MSVC x64 Build Tools required.'}
$tool=Get-ChildItem -LiteralPath (Join-Path $vs 'VC\Tools\MSVC') -Directory|Sort-Object Name -Descending|Select-Object -First 1
$kits='C:\Program Files (x86)\Windows Kits\10'
$sdk=Get-ChildItem -LiteralPath (Join-Path $kits 'Include') -Directory|Sort-Object Name -Descending|Select-Object -First 1
$cl=Join-Path $tool.FullName 'bin\Hostx64\x64\cl.exe'
$includes=@(('/I'+(Join-Path $tool.FullName 'include')),('/I'+(Join-Path $sdk.FullName 'ucrt')),('/I'+(Join-Path $sdk.FullName 'shared')),('/I'+(Join-Path $sdk.FullName 'um')))
$libs=@(('/LIBPATH:'+(Join-Path $tool.FullName 'lib\x64')),('/LIBPATH:'+(Join-Path $kits ('Lib\'+$sdk.Name+'\ucrt\x64'))),('/LIBPATH:'+(Join-Path $kits ('Lib\'+$sdk.Name+'\um\x64'))))
$common=@('/nologo','/O2','/MT','/W4','/WX','/EHsc','/std:c++17','/DUNICODE','/D_UNICODE','/Zi')+$includes
Push-Location $out
try {
    & $cl @common /LD (Join-Path $PSScriptRoot 'OfflineTeamBuilderNative.cpp') /Fe:OfflineTeamBuilderNative.dll /link @libs /DEBUG /INCREMENTAL:NO
    if($LASTEXITCODE -ne 0){throw 'Native helper build failed.'}
    foreach($test in @('CapacityAdmissionChecks','InstanceRegistryChecks')) {
        & $cl @common (Join-Path $PSScriptRoot ($test+'.cpp')) (('/Fe:')+$test+'.exe') /link @libs /DEBUG /INCREMENTAL:NO
        if($LASTEXITCODE -ne 0){throw ($test+' build failed.')}
        & (Join-Path $out ($test+'.exe'))
        if($LASTEXITCODE -ne 0){throw ($test+' failed.')}
    }
} finally {Pop-Location}
$sha=(Get-FileHash -LiteralPath (Join-Path $out 'OfflineTeamBuilderNative.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
@{abi=1;file='OfflineTeamBuilderNative.dll';sha256=$sha;statusMagic=1330922055;statusSize=128;registryEntries=14}|
    ConvertTo-Json|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'native-helper-manifest.json') -Encoding UTF8
Write-Output ('Native helper checked: '+$sha)
