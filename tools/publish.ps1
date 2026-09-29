[CmdletBinding()]
param(
    [ValidateSet('All', 'Api', 'Client')][string]$Target = 'All',
    [switch]$Migrate,
    [switch]$BuildOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$apiRoot = Split-Path $PSScriptRoot -Parent
$clientRoot = Join-Path (Split-Path $apiRoot -Parent) 'client'
$release = (Get-Date -Format 'yyyyMMddHHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$output = Join-Path $apiRoot ".deploy/$release"
$remote = 'root@157.230.58.33'
$sshOptions = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15', '-o', 'StrictHostKeyChecking=yes', '-o', 'ServerAliveInterval=10', '-o', 'ServerAliveCountMax=3')
function Invoke-Checked {
    param([string]$Program, [string[]]$Arguments)
    # Remote commands need no input. Detach from IDE console stdin so SSH can exit.
    # Do not apply -n to scp: its SSH subprocess uses stdin for the transfer protocol.
    if ($Program -eq 'ssh.exe') { $Arguments = @('-n', '-T') + $Arguments }
    Write-Host "[$(Get-Date -Format HH:mm:ss)] Running $Program $($Arguments -join ' ')"
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed (exit $LASTEXITCODE). Deployment stopped." }
}
if ($Migrate -and $Target -eq 'Client') { throw '-Migrate requires Api or All.' }
$requiredTools = @('tar.exe','ssh.exe','scp.exe')
if ($Target -ne 'Client') { $requiredTools += 'dotnet' }
if ($Target -ne 'Api') { $requiredTools += @('npm.cmd','robocopy.exe') }
foreach ($tool in $requiredTools) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Missing required tool: $tool" }
}
if (-not $BuildOnly) {
    Write-Host 'Checking SSH access to the Droplet...'
    Invoke-Checked ssh.exe ($sshOptions + @($remote, 'test -r /etc/devhub/appsettings.Production.json && systemctl is-active nginx'))
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location $apiRoot
try {
    if ($Target -ne 'Client') {
        Write-Host 'Building API...'
        Invoke-Checked dotnet @('publish','src/DevHub.Api/DevHub.Api.csproj','-c','Release','-r','linux-x64','--self-contained','true','-o',"$output/api")
        # Never package local environment overrides as production settings.
        Get-ChildItem "$output/api" -Filter 'appsettings.*.json' | Remove-Item
        Write-Host 'Packaging API...'
        Invoke-Checked tar.exe @('-czf',"$output/api.tar.gz",'-C',"$output/api",'.')
        if ($Migrate) {
            Invoke-Checked dotnet @('tool','restore')
            Invoke-Checked dotnet @('ef','migrations','bundle','--project','src/DevHub.Infrastructure','--startup-project','src/DevHub.Api','--configuration','Release','-r','linux-x64','--self-contained','-o',"$output/efbundle",'--force')
        }
    }
    if ($Target -ne 'Api') {
        $names = @('VITE_API_BASE_URL','VITE_OAUTH_API_BASE_URL','VITE_OAUTH_ENABLED')
        $saved = @{}
        foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
        $clientBuild = Join-Path $output 'client-source'
        & robocopy.exe $clientRoot $clientBuild /E /XD node_modules dist .git .idea /XF '.env*' /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw 'Unable to stage the client sources.' }
        Push-Location $clientBuild
        try {
            $env:VITE_API_BASE_URL='/api'
            $env:VITE_OAUTH_API_BASE_URL='/api'
            $env:VITE_OAUTH_ENABLED='true'
            Invoke-Checked npm.cmd @('ci')
            Invoke-Checked npm.cmd @('run','build','--','--outDir',"$output/client")
            Invoke-Checked tar.exe @('-czf',"$output/client.tar.gz",'-C',"$output/client",'.')
        } finally {
            Pop-Location
            foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$saved[$name],'Process') }
        }
    }
    if ($BuildOnly) { Write-Host "Build verified: $output"; return }
    Write-Host "Preparing release $release on the Droplet..."
    Invoke-Checked ssh.exe ($sshOptions + @($remote,"mkdir -p /opt/devhub/releases/$release"))
    $files = @(Get-ChildItem $output -File | Where-Object { $_.Name -in @('api.tar.gz','client.tar.gz','efbundle') } | ForEach-Object FullName)
    $files += Join-Path $PSScriptRoot 'publish-remote.sh'
    Write-Host 'Uploading release files (API upload can take a few minutes)...'
    Invoke-Checked scp.exe ($sshOptions + $files + @("${remote}:/opt/devhub/releases/$release/"))
    Write-Host 'Activating release and checking API startup...'
    Invoke-Checked ssh.exe ($sshOptions + @($remote,"sed -i 's/\r$//' /opt/devhub/releases/$release/publish-remote.sh && bash /opt/devhub/releases/$release/publish-remote.sh $release"))
    Write-Host 'Checking public HTTPS endpoint...'
    $response = Invoke-WebRequest 'https://157.230.58.33/login' -UseBasicParsing -TimeoutSec 30
    if ($response.StatusCode -ne 200) { throw 'Public HTTPS check failed.' }
    Write-Host 'Published successfully: https://157.230.58.33'
} finally { Pop-Location }
