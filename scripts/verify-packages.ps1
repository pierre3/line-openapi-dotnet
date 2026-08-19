#!/usr/bin/env pwsh
# Pack smoke test (follow-up from the 2026-07-14 Line.OpenApi.Bot gate review).
#
# Runs `dotnet pack` on the solution and asserts the produced packages match expectations,
# guarding against silent packaging regressions that build+test cannot catch, e.g.:
#   - an internal dependency being dropped or gaining PrivateAssets=all (stops flowing to consumers),
#     including a break of the one-way dependency graph (every code package -> Line.OpenApi.Core only)
#   - the Bot meta-package regaining a lib/ assembly (IncludeBuildOutput flips to true)
#   - an empty snupkg reappearing for the Bot meta-package (IncludeSymbols flips to true)
#   - a packable project silently dropping out, or samples/tests leaking in (IsPackable regression)
#
# Scope note: only the internal Line.OpenApi.* dependency graph and lib/snupkg layout are asserted.
# External dependency versions (Kiota etc.) and package version values are intentionally out of scope
# (the NuGet audit gate in the build-test job covers vulnerable versions; layout is version-independent,
# so release.yml's versioned pack produces the same layout this asserts).
#
# Runnable locally (`pwsh scripts/verify-packages.ps1`) and from CI. Exits non-zero on any failure.

[CmdletBinding()]
param(
    [string]$Solution = "$PSScriptRoot/../LineOpenApi.slnx",
    [string]$Configuration = 'Release',
    [string]$OutputDir = "$PSScriptRoot/../artifacts/pack-verify"
)

$ErrorActionPreference = 'Stop'

# --- Expected package contract -------------------------------------------------
$expectedTfm = 'net10.0'

# Internal (Line.OpenApi.*) dependency graph per package. External deps (Kiota etc.) are
# not listed here; only the internal graph is asserted (enforces the one-way dependency ADR).
$expectedInternalDeps = @{
    'Line.OpenApi.Core'                = @()
    'Line.OpenApi.ChannelAccessToken'  = @('Line.OpenApi.Core')
    'Line.OpenApi.Messaging'           = @('Line.OpenApi.Core')
    'Line.OpenApi.Messaging.Webhook'   = @('Line.OpenApi.Core')
    'Line.OpenApi.Liff'                = @('Line.OpenApi.Core')
    'Line.OpenApi.Login'               = @('Line.OpenApi.Core')
    'Line.OpenApi.MiniApp'             = @('Line.OpenApi.Core')
    'Line.OpenApi.Insight'             = @('Line.OpenApi.Core')
    'Line.OpenApi.Module'              = @('Line.OpenApi.Core')
    'Line.OpenApi.Shop'                = @('Line.OpenApi.Core')
    'Line.OpenApi.ManageAudience'      = @('Line.OpenApi.Core')
    # Meta-package: bundles the Bot trio (no Core direct; it flows transitively). Design section 4.2.
    'Line.OpenApi.Bot'                 = @('Line.OpenApi.ChannelAccessToken', 'Line.OpenApi.Messaging', 'Line.OpenApi.Messaging.Webhook')
}
# The meta-package ships no assembly and no symbol package; every other package must ship both.
$metaPackage = 'Line.OpenApi.Bot'

$errors = New-Object System.Collections.Generic.List[string]
function Fail([string]$msg) { $script:errors.Add($msg) }

# --- Helpers -------------------------------------------------------------------
# System.IO.Compression.ZipFile resolves from the shared framework on PowerShell 7; no Add-Type needed.
function Open-Nupkg([string]$nupkgPath) {
    return [System.IO.Compression.ZipFile]::OpenRead($nupkgPath)
}

# Reads the authoritative package id and the entry list from a .nupkg in one pass.
function Read-Package([string]$nupkgPath) {
    $zip = Open-Nupkg $nupkgPath
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName })
        $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        if (-not $nuspecEntry) { throw "No .nuspec found in $nupkgPath" }
        $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
        try { $nuspec = [xml]$reader.ReadToEnd() } finally { $reader.Close() }
        return [pscustomobject]@{
            Id      = $nuspec.package.metadata.id
            Entries = $entries
            Nuspec  = $nuspec
        }
    } finally { $zip.Dispose() }
}

# Namespace-agnostic dependency id extraction for the given target framework group.
function Get-DependencyIds([xml]$nuspec, [string]$tfm) {
    $ids = @()
    foreach ($node in $nuspec.GetElementsByTagName('dependency')) {
        $group = $node.ParentNode.Attributes['targetFramework']
        if ($group -and $group.Value -ne $tfm) { continue }
        $ids += $node.Attributes['id'].Value
    }
    return @($ids)
}

function Get-InternalDeps([xml]$nuspec, [string]$tfm) {
    return @(Get-DependencyIds $nuspec $tfm | Where-Object { $_ -like 'Line.OpenApi.*' } | Sort-Object)
}

# --- Pack ----------------------------------------------------------------------
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path

Write-Host "==> dotnet pack ($Configuration) -> $OutputDir"
# Exclude the CLI/MCP tool package (Line.OpenApi.Tools): it is released on its own cadence
# (tag tools-v*) with a different layout (DotnetTool, bundled deps, no lib/). This smoke test
# guards only the ten library packages and their one-way dependency ADR.
dotnet pack $Solution --configuration $Configuration --output $OutputDir -p:ExcludeToolFromPack=true
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed with exit code $LASTEXITCODE" }

$nupkgs = @(Get-ChildItem $OutputDir -Filter '*.nupkg')
$snupkgs = @(Get-ChildItem $OutputDir -Filter '*.snupkg')
Write-Host "==> Produced $($nupkgs.Count) nupkg / $($snupkgs.Count) snupkg"

# --- Count / membership --------------------------------------------------------
$expectedPackages = @($expectedInternalDeps.Keys)
$produced = @{}
foreach ($nupkg in $nupkgs) {
    $pkg = Read-Package $nupkg.FullName
    # snupkg shares the nupkg file's base name (id + version).
    $snupkgPath = $nupkg.FullName -replace '\.nupkg$', '.snupkg'
    $produced[$pkg.Id] = [pscustomobject]@{ Package = $pkg; HasSnupkg = (Test-Path $snupkgPath) }
}

if ($produced.Count -ne $expectedPackages.Count) {
    Fail "Expected $($expectedPackages.Count) packages but found $($produced.Count): $(($produced.Keys | Sort-Object) -join ', ')"
}
foreach ($id in $expectedPackages) {
    if (-not $produced.ContainsKey($id)) { Fail "Missing expected package: $id" }
}
foreach ($id in $produced.Keys) {
    if ($expectedPackages -notcontains $id) { Fail "Unexpected package produced (samples/tests should be IsPackable=false): $id" }
}

# --- Per-package assertions ----------------------------------------------------
foreach ($id in $produced.Keys) {
    $info = $produced[$id]
    $entries = $info.Package.Entries
    $libEntries = @($entries | Where-Object { $_ -like 'lib/*' })

    # README embedding (PackageReadmeFile) applies to every package.
    if ($entries -notcontains 'README.md') { Fail "$id must embed README.md (PackageReadmeFile)" }

    # Internal dependency graph must match exactly (enforces one-way dependency ADR / Bot bundle).
    if ($expectedInternalDeps.ContainsKey($id)) {
        $expected = @($expectedInternalDeps[$id] | Sort-Object)
        $actual = Get-InternalDeps $info.Package.Nuspec $expectedTfm
        if (($actual -join ',') -ne ($expected -join ',')) {
            Fail "$id internal dependencies mismatch. expected [$($expected -join ', ')] but got [$($actual -join ', ')]"
        }
    }

    if ($id -eq $metaPackage) {
        # Meta package: no assembly, no symbol package.
        if ($libEntries.Count -ne 0) { Fail "$id must not ship a lib/ assembly (found: $($libEntries -join ', '))" }
        if ($info.HasSnupkg) { Fail "$id must not produce a snupkg (empty symbol package)" }
    }
    else {
        # Code package: must ship a lib assembly for the TFM and a symbol package.
        $hasLibDll = @($libEntries | Where-Object { $_ -like "lib/$expectedTfm/*.dll" }).Count -gt 0
        if (-not $hasLibDll) { Fail "$id must ship lib/$expectedTfm/*.dll (lib entries: $($libEntries -join ', '))" }
        if (-not $info.HasSnupkg) { Fail "$id must produce a snupkg" }
    }
}

# --- Extensions.AI dedicated check (design section 3.4) ------------------------
# The AI package (tools/Line.OpenApi.Extensions.AI) is excluded from the library contract above
# (ExcludeToolFromPack=true), because unlike the src/** packages it depends on Messaging +
# Microsoft.Extensions.AI.Abstractions rather than Core only. Verify its exact dependency shape and
# lib/snupkg layout on its own here.
$aiProject = "$PSScriptRoot/../tools/Line.OpenApi.Extensions.AI/Line.OpenApi.Extensions.AI.csproj"
$aiOut = Join-Path $OutputDir 'ai'
New-Item -ItemType Directory -Path $aiOut -Force | Out-Null
Write-Host "==> dotnet pack Line.OpenApi.Extensions.AI -> $aiOut"
dotnet pack $aiProject --configuration $Configuration --output $aiOut
if ($LASTEXITCODE -ne 0) { throw "dotnet pack (Extensions.AI) failed with exit code $LASTEXITCODE" }

$aiNupkgs = @(Get-ChildItem $aiOut -Filter '*.nupkg')
if ($aiNupkgs.Count -ne 1) {
    Fail "Extensions.AI: expected exactly 1 nupkg but found $($aiNupkgs.Count)"
}
else {
    $aiPkg = Read-Package $aiNupkgs[0].FullName
    if ($aiPkg.Id -ne 'Line.OpenApi.Extensions.AI') { Fail "Extensions.AI: unexpected package id '$($aiPkg.Id)'" }
    # Exact dependency set: exactly the two public dependencies (design section 3.3). This asserts
    # the full dependency list, not just the internal Line.OpenApi.* subset.
    $aiDeps = @(Get-DependencyIds $aiPkg.Nuspec $expectedTfm | Sort-Object)
    $expectedAiDeps = @('Line.OpenApi.Messaging', 'Microsoft.Extensions.AI.Abstractions') | Sort-Object
    if (($aiDeps -join ',') -ne ($expectedAiDeps -join ',')) {
        Fail "Extensions.AI dependencies mismatch. expected [$($expectedAiDeps -join ', ')] but got [$($aiDeps -join ', ')]"
    }
    if ($aiPkg.Entries -notcontains 'README.md') { Fail "Extensions.AI must embed README.md (PackageReadmeFile)" }
    $aiHasLib = @($aiPkg.Entries | Where-Object { $_ -like "lib/$expectedTfm/*.dll" }).Count -gt 0
    if (-not $aiHasLib) { Fail "Extensions.AI must ship lib/$expectedTfm/*.dll" }
    $aiSnupkgPath = $aiNupkgs[0].FullName -replace '\.nupkg$', '.snupkg'
    if (-not (Test-Path $aiSnupkgPath)) { Fail "Extensions.AI must produce a snupkg" }
}

# --- Report --------------------------------------------------------------------
if ($errors.Count -gt 0) {
    Write-Host ""
    Write-Host "PACK SMOKE TEST FAILED:" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

$codeCount = $expectedPackages.Count - 1
Write-Host ""
Write-Host "Pack smoke test PASSED: $codeCount code packages (lib + snupkg) + 1 meta package (${metaPackage}: no lib, no snupkg, 3 deps). Internal dependency graph verified." -ForegroundColor Green
Write-Host "Extensions.AI dedicated check PASSED: Line.OpenApi.Extensions.AI (lib + snupkg, deps = Line.OpenApi.Messaging + Microsoft.Extensions.AI.Abstractions)." -ForegroundColor Green
