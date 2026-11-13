[CmdletBinding()]
param(
    [switch]$NoBuild,
    [ValidateSet("Local", "Production")]
    [string]$Environment = "Local"
)

$ErrorActionPreference = "Stop"

$repositoryPath = $PSScriptRoot
$apiPath = Join-Path $repositoryPath "src\DevHub.Api"
$projectPath = Join-Path $apiPath "DevHub.Api.csproj"
$pathDotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
$userDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
$dotnetExecutable = $null

foreach ($candidate in @($pathDotnet.Source, $userDotnet) | Select-Object -Unique) {
    if (-not $candidate -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
    $sdkVersion = & $candidate --version | Select-Object -Last 1
    if ($LASTEXITCODE -eq 0 -and $sdkVersion -match '^(\d+)\.' -and [int]$Matches[1] -ge 11) {
        $dotnetExecutable = $candidate
        break
    }
}

if (-not $dotnetExecutable) {
    throw "Install the .NET 11 SDK to run the DevHub API."
}

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

$runArguments = @("run", "--project", $projectPath, "--launch-profile", $Environment)

if ($NoBuild)
{
    $runArguments += "--no-build"
}

& $dotnetExecutable @runArguments
exit $LASTEXITCODE
