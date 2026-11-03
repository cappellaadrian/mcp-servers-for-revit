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
    'revara-mcp.addin',
    'LICENSE',
    'NOTICE',
    'revara_mcp/RevitMCPPlugin.dll',
    'revara_mcp/Commands/RevitMCPCommandSet/command.json',
    "revara_mcp/Commands/RevitMCPCommandSet/$Year/RevitMCPCommandSet.dll"
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

# 2b. Identity: the add-in manifest, and no leftover of the upstream names.
$addinFail = @()
try {
    [xml]$addinXml = Get-Content -LiteralPath (Join-Path $Root 'revara-mcp.addin') -Raw -Encoding UTF8
    $addinNode = $addinXml.RevitAddIns.AddIn
    $expectedAddin = [ordered]@{
        Name              = 'Revara MCP'
        ClientId          = 'BBBF91F5-174C-47CA-B26A-B6A6826923B7'
        VendorId          = 'Revara'
        VendorDescription = 'https://github.com/cappellaadrian/mcp-servers-for-revit'
        FullClassName     = 'revit_mcp_plugin.Core.Application'
        Assembly          = 'revara_mcp/RevitMCPPlugin.dll'
    }
    foreach ($key in $expectedAddin.Keys) {
        $actual = [string]$addinNode.$key
        if ($actual -cne $expectedAddin[$key]) {
            Write-Host "BAD ADDIN: $key is '$actual', expected '$($expectedAddin[$key])'"
            $addinFail += "addin $key"
        }
    }
    $assemblyPath = Join-Path $Root ([string]$addinNode.Assembly)
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        Write-Host "BAD ADDIN: Assembly path does not exist under the root: $($addinNode.Assembly)"
        $addinFail += 'addin Assembly path'
    }
} catch {
    Write-Host "BAD ADDIN: revara-mcp.addin could not be parsed: $($_.Exception.Message)"
    $addinFail += 'addin parse'
}
if (Test-Path -LiteralPath (Join-Path $Root 'mcp-servers-for-revit.addin')) {
    Write-Host 'BAD ADDIN: mcp-servers-for-revit.addin must not exist'
    $addinFail += 'old addin file present'
}
if (Test-Path -LiteralPath (Join-Path $Root 'revit_mcp_plugin')) {
    Write-Host 'BAD ADDIN: folder revit_mcp_plugin must not exist'
    $addinFail += 'old addin folder present'
}
if ($addinFail.Count -gt 0) {
    Write-Host ("check-addin FAILED: " + ($addinFail -join '; '))
    exit 1
}

# 3. Product version of the plugin. Exactly the expected version, optionally followed by +<build metadata>.
$pluginDll = Join-Path $Root 'revara_mcp/RevitMCPPlugin.dll'
$productVersion = (Get-Item -LiteralPath $pluginDll).VersionInfo.ProductVersion
Write-Host "ProductVersion: $productVersion"
if (-not $productVersion -or -not ($productVersion -ceq $ExpectedVersion -or $productVersion.StartsWith($ExpectedVersion + '+', [System.StringComparison]::Ordinal))) {
    Write-Host "BAD VERSION: expected $ExpectedVersion, got $productVersion"
    $failures += "ProductVersion $productVersion is not $ExpectedVersion"
}

# 4. Target framework string inside each DLL (bytes searched as UTF-8 text, assembly not loaded).
$dlls = @(
    'revara_mcp/RevitMCPPlugin.dll',
    "revara_mcp/Commands/RevitMCPCommandSet/$Year/RevitMCPCommandSet.dll"
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
