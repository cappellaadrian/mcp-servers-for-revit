# Revara fork: what each Revit year's add-in must contain. Called by CI from the repo root:
#   ./scripts/ci-check.ps1 -Year <year>
param(
    [Parameter(Mandatory = $true)][string]$Year
)

$rows = @{
    '2025' = @{ Root = 'plugin/bin/AddIn 2025 Release R25'; ExpectedVersion = '1.0.0-revara.2'; ExpectedTfm = '.NETCoreApp,Version=v8.0' }
    '2026' = @{ Root = 'plugin/bin/AddIn 2026 Release R26'; ExpectedVersion = '1.0.0-revara.2'; ExpectedTfm = '.NETCoreApp,Version=v8.0' }
    '2027' = @{ Root = 'plugin/bin/AddIn 2027 Release R27'; ExpectedVersion = '1.0.0-revara.2'; ExpectedTfm = '.NETCoreApp,Version=v10.0' }
}

if (-not $rows.ContainsKey($Year)) {
    Write-Host "ci-check: unknown year $Year"
    exit 1
}

$row = $rows[$Year]
& (Join-Path $PSScriptRoot 'check-addin.ps1') -Root $row.Root -Year $Year -ExpectedVersion $row.ExpectedVersion -ExpectedTfm $row.ExpectedTfm
exit $LASTEXITCODE
