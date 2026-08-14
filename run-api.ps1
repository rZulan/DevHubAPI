[CmdletBinding()]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repositoryPath = $PSScriptRoot
$apiPath = Join-Path $repositoryPath "src\DevHub.Api"
$projectPath = Join-Path $apiPath "DevHub.Api.csproj"
$developmentPorts = 5288, 7116
$listenerProcessIds = Get-NetTCPConnection `
    -State Listen `
    -LocalPort $developmentPorts `
    -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique

foreach ($listenerProcessId in $listenerProcessIds)
{
    $process = Get-Process -Id $listenerProcessId -ErrorAction Stop
    $processPath = $process.Path
    $isDevHubApi =
        $process.ProcessName -eq "DevHub.Api" -and
        $processPath -and
        $processPath.StartsWith($apiPath, [StringComparison]::OrdinalIgnoreCase)

    if (-not $isDevHubApi)
    {
        throw "Development port is occupied by $($process.ProcessName) " +
              "(PID $listenerProcessId). It was not stopped because it is not " +
              "this repository's DevHub.Api process."
    }

    Write-Host "Stopping existing DevHub.Api process (PID $listenerProcessId)..."
    Stop-Process -Id $listenerProcessId
    Wait-Process -Id $listenerProcessId -Timeout 10 -ErrorAction SilentlyContinue
}

$runArguments = @("run", "--project", $projectPath)

if ($NoBuild)
{
    $runArguments += "--no-build"
}

& dotnet @runArguments
exit $LASTEXITCODE
