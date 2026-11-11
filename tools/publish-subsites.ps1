[CmdletBinding()]
param(
    [ValidateSet('All', 'Admin', 'Docs')][string]$Target = 'All',
    [switch]$Configure,
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Configure -and ($Target -eq 'Docs' -or $BuildOnly)) { throw '-Configure requires an admin deployment.' }
$workspace = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$client = Join-Path $workspace 'client'
$docs = Join-Path $workspace 'docs'
$sites = if ($Target -eq 'All') { @('admin', 'docs') } else { @($Target.ToLowerInvariant()) }
$serverIp = '157.230.58.33'
$server = "root@$serverIp"
$release = (Get-Date -Format 'yyyyMMddHHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$staging = Join-Path $PSScriptRoot "../.deploy/subsites-$release"
$remoteStage = "/opt/devhub/releases/subsites-$release"
$sshOptions = @('-o','BatchMode=yes','-o','ConnectTimeout=15','-o','StrictHostKeyChecking=yes')
$adminId = '-'

function Invoke-Checked {
    param([string]$Program, [string[]]$Arguments)
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed (exit $LASTEXITCODE)." }
}

if (-not $BuildOnly) {
    foreach ($site in $sites) {
        $hostname = "$site.dev-hub.site"
        try {
            $addresses = @(Resolve-DnsName -Name $hostname -Type A -DnsOnly -NoHostsFile -ErrorAction Stop |
                Where-Object Type -eq 'A' | Select-Object -ExpandProperty IPAddress)
        } catch {
            throw "DNS lookup failed for $hostname. $($_.Exception.Message)"
        }
        if ($addresses.Count -eq 0 -or @($addresses | Where-Object { $_ -ne $serverIp }).Count -gt 0) {
            $resolved = if ($addresses.Count -eq 0) { 'no A records' } else { $addresses -join ', ' }
            throw "$hostname must resolve to $serverIp. Windows DNS returned $resolved."
        }
    }
    $preflight = @(
        'export PATH=$PATH:/snap/bin:/opt/certbot/bin'
        'for service in nginx devhub; do systemctl is-active --quiet $service || { printf ''%s is not active.\n'' $service >&2; exit 1; }; done'
        'command -v certbot >/dev/null || { echo ''Certbot was not found on the Droplet. Install it there before publishing.'' >&2; exit 1; }'
        'certbot --version'
    ) -join '; '
    Invoke-Checked ssh.exe (@('-n','-T') + $sshOptions + @($server, $preflight))
    if ($Configure) {
        $enteredId = Read-Host 'Approved Zevont account ID (GUID from https://dev-hub.site/api/users/me)'
        $parsedId = [guid]::Empty
        if (-not [guid]::TryParse($enteredId, [ref]$parsedId) -or $parsedId -eq [guid]::Empty) { throw 'Enter a valid Zevont account ID.' }
        $adminId = $parsedId.ToString('D')
    }
}

New-Item -ItemType Directory -Path $staging -Force | Out-Null
$uploads = @()
foreach ($site in $sites) {
    $buildRoot = $client
    $buildScript = "build:$site"
    $output = Join-Path $client "sites/$site/dist"
    $installedPackage = 'node_modules/vite/package.json'
    if ($site -eq 'docs' -and (Test-Path -LiteralPath (Join-Path $docs 'build.mjs'))) {
        $buildRoot = $docs
        $buildScript = 'build'
        $output = Join-Path $docs 'dist'
        $installedPackage = 'node_modules/react-markdown/package.json'
    }

    Push-Location $buildRoot
    try {
        if (-not (Test-Path -LiteralPath $installedPackage)) { Invoke-Checked npm.cmd @('ci') }
        Invoke-Checked npm.cmd @('run', $buildScript)
        $archive = Join-Path $staging "$site.tar.gz"
        Invoke-Checked tar.exe @('-czf', $archive, '-C', $output, '.')
        $uploads += (Resolve-Path -LiteralPath $archive).Path
    } finally { Pop-Location }
}

if ($BuildOnly) { Write-Host "Subsite packages built in $staging"; return }

$uploads += Join-Path $PSScriptRoot 'publish-subsites-remote.sh'
Invoke-Checked ssh.exe (@('-n','-T') + $sshOptions + @($server, "install -d -m 700 $remoteStage"))
Invoke-Checked scp.exe ($sshOptions + $uploads + @("${server}:$remoteStage/"))
Invoke-Checked ssh.exe (@('-n','-T') + $sshOptions + @($server,
    "sed -i 's/\r$//' $remoteStage/publish-subsites-remote.sh && bash $remoteStage/publish-subsites-remote.sh $release $adminId $($sites -join ' ')"))

if ($Configure) { & (Join-Path $PSScriptRoot 'publish.ps1') -Target Api }
foreach ($site in $sites) {
    $url = "https://$site.dev-hub.site/"
    Invoke-Checked curl.exe @('--fail', '--silent', '--show-error', '--max-time', '20', '--output', 'NUL', $url)
    if ($site -eq 'admin') {
        $status = & curl.exe --silent --show-error --max-time 20 --output NUL --write-out '%{http_code}' "${url}api/admin/overview"
        if ($LASTEXITCODE -ne 0 -or $status -ne '401') { throw 'Admin API check failed. The unauthenticated endpoint must return HTTP 401.' }
    }
    Write-Host "Published $url"
}
