# Revara fork: checks one built add-in folder. Only ci-check.ps1 calls it, with the values of its table.
param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$Year,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion,
    [Parameter(Mandatory = $true)][string]$ExpectedTfm
)

$ErrorActionPreference = 'Stop'

# 1. List every file under the root.
if (Test-Path -LiteralPath $Root) {
    Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object { $_.FullName.Substring((Resolve-Path -LiteralPath $Root).Path.Length).TrimStart('\', '/') }
} else {
    Write-Host "root folder not found: $Root"
}

# 2. Required files. Any missing one fails before a DLL is read.
$required = @(
    'mcp-servers-for-revit.addin',
    'LICENSE',
    'NOTICE',
    'revit_mcp_plugin/RevitMCPPlugin.dll',
    'revit_mcp_plugin/Commands/RevitMCPCommandSet/command.json',
    "revit_mcp_plugin/Commands/RevitMCPCommandSet/$Year/RevitMCPCommandSet.dll"
)
$failures = @()
foreach ($rel in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root $rel) -PathType Leaf)) {
        Write-Host "MISSING: $rel"
        $failures += "missing $rel"
    }
}
if ($failures.Count -gt 0) {
    Write-Host ("check-addin FAILED: " + ($failures -join '; '))
    exit 1
}

# 3. Product version of the plugin.
$pluginDll = Join-Path $Root 'revit_mcp_plugin/RevitMCPPlugin.dll'
$productVersion = (Get-Item -LiteralPath $pluginDll).VersionInfo.ProductVersion
Write-Host "ProductVersion: $productVersion"
if (-not $productVersion -or -not $productVersion.StartsWith($ExpectedVersion)) {
    Write-Host "BAD VERSION: expected $ExpectedVersion, got $productVersion"
    $failures += "ProductVersion $productVersion does not start with $ExpectedVersion"
}

# 4. Target framework string inside each DLL (bytes searched as UTF-8 text, assembly not loaded).
$dlls = @(
    'revit_mcp_plugin/RevitMCPPlugin.dll',
    "revit_mcp_plugin/Commands/RevitMCPCommandSet/$Year/RevitMCPCommandSet.dll"
)
foreach ($rel in $dlls) {
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path (Resolve-Path -LiteralPath $Root).Path $rel))
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($text.Contains($ExpectedTfm)) {
        Write-Host "TFM ok: $rel contains $ExpectedTfm"
    } else {
        Write-Host "BAD TFM: $rel does not contain $ExpectedTfm"
        $failures += "$rel does not contain $ExpectedTfm"
    }
}

if ($failures.Count -gt 0) {
    Write-Host ("check-addin FAILED: " + ($failures -join '; '))
    exit 1
}
Write-Host "check-addin OK $Year $ExpectedVersion $ExpectedTfm"
exit 0
