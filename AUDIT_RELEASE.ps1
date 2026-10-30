param(
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [string]$ZipPath = ""
)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackagePath).Path.TrimEnd('\', '/')
$allowed = @(
    'TF2IdleManager.exe',
    'README.md', 'PORTABLE.md', 'RELEASE-NOTES.txt',
    'assets/TF2IdleManager.ico', 'assets/TF2IdleManager.png'
)
function Assert-Paths([string[]]$paths, [string]$source) {
    $normalized = @($paths | ForEach-Object { $_.Replace([char]92,[char]47).TrimStart([char]47) })
    $unexpected = @($normalized | Where-Object { $allowed -cnotcontains $_ })
    if ($unexpected.Count -gt 0) { throw "$source contains unexpected files: $($unexpected -join ', ')" }
    foreach ($required in $allowed) {
        if ($normalized -cnotcontains $required) { throw "$source missing: $required" }
    }
    Write-Host "[PASS] $source file allowlist ($($normalized.Count) files)" -ForegroundColor Green
}
$files = @(Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object { $_.FullName.Substring($package.Length).TrimStart([char]92,[char]47).Replace([char]92,[char]47) })
Assert-Paths $files 'Portable directory'
$exe = Join-Path $package 'TF2IdleManager.exe'
$stream = [System.IO.File]::OpenRead($exe)
try {
    if ($stream.Length -lt 1000000 -or $stream.ReadByte() -ne 77 -or $stream.ReadByte() -ne 90) {
        throw 'Portable EXE is not a plausible PE executable.'
    }
} finally { $stream.Dispose() }
Write-Host '[PASS] Portable EXE has MZ header and reasonable size' -ForegroundColor Green
if ($ZipPath -ne '') {
    if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) { throw "ZIP not found: $ZipPath" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ZipPath).Path)
    try {
        $zipNames = @($archive.Entries | Where-Object { $_.Name -ne '' } | ForEach-Object { $_.FullName.Replace([char]92,[char]47).TrimStart([char]47) })
        if (@($zipNames | Where-Object { $_ -match '(^|/)\.\.?(/|$)' }).Count -gt 0) { throw 'ZIP contains unsafe traversal paths.' }
        Assert-Paths $zipNames 'Portable ZIP'
    } finally { $archive.Dispose() }
}
Write-Host '[PASS] No API keys, logs, settings, account histories, CSV, build files or sources in distribution' -ForegroundColor Green
