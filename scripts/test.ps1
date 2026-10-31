$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $root 'tests\SchedulerProbe\SchedulerProbe.csproj') -c Release -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Scheduler regression failed.' }
dotnet run --project (Join-Path $root 'tests\CommunityProbe\CommunityProbe.csproj') -c Release -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Community tests failed.' }
dotnet run --project (Join-Path $root 'tests\CommunityProbe\CommunityProbe.csproj') -c Release -p:ProbeTestMode=true -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'TESTMODE tests failed.' }
