param([string]$OutputDirectory = "", [string]$Version = "", [string]$SourceCommit = "")
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'artifacts\release' }
$project = Join-Path $root 'src\Cet6WordMachine\Cet6WordMachine.csproj'
$publishArgs = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:UseSharedCompilation=false', '-o', $OutputDirectory)
if ($Version) { $publishArgs += "-p:Version=$Version" }
if ($Version -and $SourceCommit) { $publishArgs += "-p:InformationalVersion=$Version+$SourceCommit"; $publishArgs += '-p:IncludeSourceRevisionInInformationalVersion=false' }
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination (Join-Path $OutputDirectory 'licenses') -Recurse -Force
Write-Host "Built: $OutputDirectory"
