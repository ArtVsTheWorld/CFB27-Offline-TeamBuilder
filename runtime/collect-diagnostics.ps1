[CmdletBinding()]
param(
    [string]$MmcRoot,
    [string]$OutputDirectory,
    [string]$DiagnosticsRoot
)
$ErrorActionPreference = 'Stop'
if (!$DiagnosticsRoot) {
    if (!$MmcRoot) { throw 'Specify -MmcRoot so the installed plugin data folder can be located.' }
    $DiagnosticsRoot = Join-Path (Resolve-Path -LiteralPath $MmcRoot).Path 'Plugins\OfflineTeamBuilderRuntime'
}
if (!$OutputDirectory) { $OutputDirectory = Join-Path $DiagnosticsRoot 'Support' }
$out = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force | Out-Null
$stage = Join-Path $out ('review-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
# Explicit allow-list: no save files, cached TeamBuilder assets, memory dumps,
# executable bytes/hashes, HTTP bodies, config secrets or detailed service logs.
foreach ($name in @('runtime.log','runtime.log.1','runtime.log.2','status.json')) {
    $path = Join-Path $DiagnosticsRoot $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        try { Copy-Item -LiteralPath $path -Destination (Join-Path $stage $name) } catch { Write-Warning "Could not copy $name (file may be busy)." }
    }
}
$report = [ordered]@{collectedUtc=[DateTime]::UtcNow.ToString('O');runtime=[Environment]::Version.ToString();plugins=@()}
if ($MmcRoot) {
    $plugins = Join-Path (Resolve-Path -LiteralPath $MmcRoot).Path 'Plugins'
    foreach ($name in @('OfflineTeamBuilderRuntime.dll','OfflineTeamBuilder.dll','OfflineTeamBuilderReentry.dll')) {
        $path = Join-Path $plugins $name
        if (Test-Path -LiteralPath $path) {
            $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($path)
            $report.plugins += @{name=$name;version=$info.FileVersion;bytes=(Get-Item -LiteralPath $path).Length}
        }
    }
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'support.json') -Encoding UTF8
$zip = Join-Path $out ('OfflineTeamBuilderRuntime-support-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)+'.zip')
Compress-Archive -LiteralPath @(Get-ChildItem -LiteralPath $stage -File | ForEach-Object FullName) -DestinationPath $zip
# Leave the small review folder intact: never recursively delete a computed path.
Write-Host "Support archive: $zip"
Write-Host "Review copies: $stage"
Write-Host 'Review before sharing: diagnostics may contain Windows usernames, local paths and native addresses. No data was uploaded.'
