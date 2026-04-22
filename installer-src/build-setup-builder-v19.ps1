# Сборка: single-file касса -> publish/single-file-v19, затем установщик -> dist/setup-builder-v19
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$ico = Join-Path $root "src\NurMarketKassa\Assets\app-logo.ico"
if (-not (Test-Path $ico)) {
    & (Join-Path $PSScriptRoot "gen-app-ico.ps1")
}

$appProj = Join-Path $root "src\NurMarketKassa\NurMarketKassa.csproj"
$setupProj = Join-Path $root "installer-src\NurMarketKassa.SetupBuilder\NurMarketKassa.SetupBuilder.csproj"
$payloadDir = Join-Path $root "publish\single-file-v19"
$outDir = Join-Path $root "dist\setup-builder-v19"

$pubArgs = @(
    "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true"
)

Write-Host "Publishing app -> $payloadDir"
dotnet publish $appProj @pubArgs -o $payloadDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publishing setup -> $outDir"
dotnet publish $setupProj @pubArgs -o $outDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "OK: $(Join-Path $outDir 'NurMarketKassaSetup.exe')"
