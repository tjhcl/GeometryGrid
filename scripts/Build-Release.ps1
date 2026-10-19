[CmdletBinding()]
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$releaseInfo = Get-Content -LiteralPath (Join-Path $repoRoot 'release/version.json') -Raw | ConvertFrom-Json
$version = [string]$releaseInfo.version
if ($version -notmatch '^\d+\.\d+(?:\.\d+)?$') { throw 'Invalid release version' }
$dotnetCommand = Get-Command dotnet -ErrorAction Stop
$dotnetExe = $dotnetCommand.Source
$releaseFolder = Join-Path $repoRoot "artifacts/releases/v$version"
$packageFolder = Join-Path $repoRoot "artifacts/package/v$version"
$appFolder = Join-Path $repoRoot "artifacts/publish/v$version/app"
$cliFolder = Join-Path $repoRoot "artifacts/publish/v$version/cli"
foreach ($folder in @($releaseFolder, $packageFolder, $appFolder, $cliFolder)) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}

Push-Location $repoRoot
try {
    if (-not $SkipTests) {
        & $dotnetExe test tests/GeometryGrid.Tests/GeometryGrid.Tests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    }
    & $dotnetExe publish src/GeometryGrid.App/GeometryGrid.App.csproj -c Release -o $appFolder
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed' }
    & $dotnetExe publish src/GeometryGrid.Cli/GeometryGrid.Cli.csproj -c Release -o $cliFolder
    if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed' }
    $desktop = "GeometryGrid-v.$version.exe"
    Copy-Item -LiteralPath (Join-Path $appFolder $desktop) -Destination (Join-Path $packageFolder $desktop) -Force
    Copy-Item -LiteralPath (Join-Path $cliFolder 'GeometryGrid.Solver.exe') -Destination $packageFolder -Force
    $documents = @('README.md', '使用说明.md', 'JSON创建规范_README.md', 'AI题图转JSON提示词.md',
        'LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md', 'CONTRIBUTORS.md', 'SECURITY_AUDIT.md')
    foreach ($document in $documents) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $document) -Destination $packageFolder -Force
    }
    $exampleFolder = Join-Path $packageFolder 'examples'
    New-Item -ItemType Directory -Path $exampleFolder -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'examples/tianjin_2026_q18.json') -Destination $exampleFolder -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src/GeometryGrid.App/Assets/Fonts/NotoSansCJK-OFL.txt') -Destination $packageFolder -Force

    # Package an explicit allowlist: never include bin/obj, PDB, preferences, logs or arbitrary local files.
    $allowed = @($desktop, 'GeometryGrid.Solver.exe') + $documents + @('NotoSansCJK-OFL.txt', 'examples/tianjin_2026_q18.json')
    $zipPath = Join-Path $releaseFolder "GeometryGrid-v.$version-win-x64.zip"
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $fileStream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($fileStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($name in $allowed) {
                $source = Join-Path $packageFolder $name
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $source, $name.Replace('\', '/'),
                    [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $archive.Dispose() }
    } finally { $fileStream.Dispose() }
    $sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText((Join-Path $releaseFolder 'SHA256SUMS.txt'),
        "$sha256  $([System.IO.Path]::GetFileName($zipPath))`n", [System.Text.UTF8Encoding]::new($false))
    Write-Output "Package: $zipPath"
    Write-Output "SHA256: $sha256"
} finally { Pop-Location }
