$ErrorActionPreference='Stop'
$out=Join-Path $PSScriptRoot 'bin';New-Item -ItemType Directory -Path $out -Force|Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$tool=Get-ChildItem -LiteralPath (Join-Path $vs 'VC\Tools\MSVC') -Directory|Sort-Object Name -Descending|Select-Object -First 1
$kits='C:\Program Files (x86)\Windows Kits\10';$sdk=Get-ChildItem -LiteralPath (Join-Path $kits 'Include') -Directory|Sort-Object Name -Descending|Select-Object -First 1
$cl=Join-Path $tool.FullName 'bin\Hostx64\x64\cl.exe'
$includes=@(('/I'+(Join-Path $tool.FullName 'include')),('/I'+(Join-Path $sdk.FullName 'ucrt')),('/I'+(Join-Path $sdk.FullName 'shared')),('/I'+(Join-Path $sdk.FullName 'um')))
$libs=@(('/LIBPATH:'+(Join-Path $tool.FullName 'lib\x64')),('/LIBPATH:'+(Join-Path $kits ('Lib\'+$sdk.Name+'\ucrt\x64'))),('/LIBPATH:'+(Join-Path $kits ('Lib\'+$sdk.Name+'\um\x64'))))
Push-Location $out
try{& $cl /nologo /O2 /MT /W3 /EHsc /std:c++17 @includes /LD (Join-Path $PSScriptRoot 'ArtworkCodec.cpp') (Join-Path $PSScriptRoot 'vendor\bc7enc.cpp') /Fe:OfflineTeamBuilder.ArtworkCodec.dll /link @libs /INCREMENTAL:NO;if($LASTEXITCODE -ne 0){throw 'Artwork codec build failed'}}finally{Pop-Location}
