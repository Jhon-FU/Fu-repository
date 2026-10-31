#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$OutputDirectory = '',
    [string]$SourceCommit = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$SourceCommit) {
    $SourceCommit = git -c "safe.directory=$($root.Replace('\', '/'))" -C $root rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source commit; pass -SourceCommit explicitly.' }
}
if ($SourceCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'SourceCommit must be a full Git commit SHA.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root "artifacts\releases\v$Version" }
if (![IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $root $OutputDirectory }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory; existing files will not be overwritten or included.' }
$stage = Join-Path $OutputDirectory 'WordMachineCommunity'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $stage -Version $Version -SourceCommit $SourceCommit

# Preserve the notices shipped with the exact runtime packs selected by publish.
$assets = Get-Content -LiteralPath (Join-Path $root 'src\Cet6WordMachine\obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$deps = Get-Content -LiteralPath (Join-Path $root 'src\Cet6WordMachine\bin\Release\net9.0-windows\win-x64\WordMachineCommunity.deps.json') -Raw | ConvertFrom-Json -AsHashtable
$runtimeNotices = @()
foreach ($runtime in $deps.libraries.Keys | Where-Object { $_ -like 'runtimepack.*' }) {
    $parts = $runtime.Split('/', 2)
    $packageId = $parts[0].Substring('runtimepack.'.Length).ToLowerInvariant()
    $packagePath = "$packageId/$($parts[1])"
    $packageDirectory = $assets.packageFolders.Keys | ForEach-Object { Join-Path $_ $packagePath } | Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
    if (!$packageDirectory) { throw "Cannot find runtime license package: $packagePath" }
    $notices = @(Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
    if (!$notices.Count) { throw "No runtime notices found: $packagePath" }
    foreach ($notice in $notices) {
        $relative = "licenses/$packageId-$($notice.Name)"
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $stage $relative)
        $runtimeNotices += $relative
    }
}

$metadata = @"
单词机社区版 v$Version
平台：Windows x64，自包含发布，无需另行安装 .NET。
源码提交：$SourceCommit
对应源码：https://github.com/Jhon-FU/Fu-repository/tree/v$Version
使用方法：完整解压后运行 WordMachineCommunity.exe，请先阅读“用前读我.txt”。
学习数据默认保存在 %LOCALAPPDATA%\WordMachineCommunity\，本包不附带个人数据。
"@
[IO.File]::WriteAllText((Join-Path $stage '版本信息.txt'), $metadata.Replace("`r`n", "`n").Replace("`n", "`r`n"), [Text.UTF8Encoding]::new($true))
$expected = @('WordMachineCommunity.exe', '用前读我.txt', '撤销开机自启动.cmd', '版本信息.txt', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'licenses/ECDICT-MIT.txt', 'licenses/Qwerty-LICENSE.txt') + $runtimeNotices
$actual = @(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\', '/') })
if (Compare-Object ($expected | Sort-Object) ($actual | Sort-Object)) { throw 'Release content differs from the public-file allowlist.' }
$exe = Get-Item -LiteralPath (Join-Path $stage 'WordMachineCommunity.exe')
if (!$exe.VersionInfo.ProductVersion.StartsWith("$Version+$SourceCommit")) { throw 'Executable version does not match this release and source commit.' }
$zip = Join-Path $OutputDirectory 'WordMachineCommunity-win-x64.zip'
Compress-Archive -LiteralPath $stage -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'), "$hash  WordMachineCommunity-win-x64.zip`n", [Text.UTF8Encoding]::new($false))
Write-Host "Packaged v$Version from $SourceCommit"
Write-Host "ZIP: $zip"
Write-Host "SHA256: $hash"
