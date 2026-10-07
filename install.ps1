# ExpertEase Installer (Windows PowerShell)
# Downloads the latest ExpertEase MCP server (with bundled knowledge bases).
#
# Usage:
#   powershell -c "irm https://raw.githubusercontent.com/sithiro/ExpertEase/main/install.ps1 | iex"
#   $env:VERSION = "1.0.3"; (then run the same command) to pin a version

$rid = "win-x64"
$repo = "sithiro/ExpertEase"

if ($env:VERSION) {
    $tag = "expertease-v$($env:VERSION)"
} else {
    Write-Host "Checking latest version..."
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -UseBasicParsing -ErrorAction Stop
        $tag = $release.tag_name
    } catch {
        Write-Error "Failed to fetch latest release from GitHub."
        Write-Error $_.Exception.Message
        exit 1
    }
}

$version = $tag -replace '^expertease-v', ''
$url = "https://github.com/$repo/releases/download/$tag/expertease-$rid.mcpb"
$installDir = "ExpertEase v$version"

Write-Host "Downloading ExpertEase v$version for $rid..."
$tmpFile = [System.IO.Path]::GetTempFileName() + ".zip"

try {
    Invoke-WebRequest -Uri $url -OutFile $tmpFile -UseBasicParsing -ErrorAction Stop
} catch {
    Write-Error "Failed to download: $url"
    Write-Error $_.Exception.Message
    exit 1
}

Write-Host "Extracting to '$installDir'..."
if (Test-Path $installDir) {
    Remove-Item $installDir -Recurse -Force
}
New-Item -ItemType Directory -Path $installDir -Force | Out-Null

# Extract server/* (binary + ExpertEase.Knowledge/) from the mcpb (zip), dropping the server/ prefix.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($tmpFile)
try {
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName -like "server/*" -and $entry.Name) {
            $relative = $entry.FullName.Substring("server/".Length)
            $destPath = Join-Path $installDir $relative
            New-Item -ItemType Directory -Path (Split-Path $destPath) -Force | Out-Null
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destPath, $true)
        }
    }
} finally {
    $zip.Dispose()
}

Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue

$binaryPath = (Resolve-Path (Join-Path $installDir "expertease.exe")).Path -replace '\\', '/'

Write-Host ""
Write-Host "ExpertEase v$version installed to '$installDir'" -ForegroundColor Green
Write-Host ""
Write-Host "Register with your agent:"
Write-Host ""
Write-Host "  claude mcp add -s user -t stdio expertease -- `"$binaryPath`"" -ForegroundColor Cyan
Write-Host "  codex mcp add expertease -- `"$binaryPath`"" -ForegroundColor Cyan
Write-Host "  code --add-mcp '{`"name`":`"expertease`",`"command`":`"$binaryPath`",`"args`":[]}'" -ForegroundColor Cyan
Write-Host ""
Write-Host "Add your own knowledge bases by setting EXPERTEASE_KNOWLEDGE_DIR to a folder of .json/.csv files."
Write-Host ""
Write-Host "To unregister:"
Write-Host ""
Write-Host "  claude mcp remove -s user expertease" -ForegroundColor DarkGray
Write-Host "  codex mcp remove expertease" -ForegroundColor DarkGray
