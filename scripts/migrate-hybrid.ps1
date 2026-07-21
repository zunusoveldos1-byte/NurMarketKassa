# Migration script: physical file moves for hybrid architecture
$ErrorActionPreference = "Stop"
$root = "c:\Users\User\Desktop\NurCrmPosKassa-master\NurCrmPosKassa-master\src"
$wpf = Join-Path $root "NurMarketKassa"
$infra = Join-Path $root "NurMarketKassa.Infrastructure"
$vms = Join-Path $root "NurMarketKassa.ViewModels"
$assets = Join-Path $root "NurMarketKassa.Assets"

function Move-Tree($relPath) {
    $src = Join-Path $wpf $relPath
    $dst = Join-Path $infra $relPath
    if (-not (Test-Path $src)) {
        Write-Host "SKIP missing: $src"
        return
    }
    $parent = Split-Path $dst -Parent
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    if (Test-Path $dst) {
        Write-Host "DST exists, merging: $dst"
        Get-ChildItem $src -Recurse -File | ForEach-Object {
            $target = $_.FullName.Replace($src, $dst)
            $tp = Split-Path $target -Parent
            if (-not (Test-Path $tp)) { New-Item -ItemType Directory -Path $tp -Force | Out-Null }
            Move-Item $_.FullName $target -Force
        }
        Remove-Item $src -Recurse -Force -ErrorAction SilentlyContinue
    } else {
        Move-Item $src $dst -Force
    }
    Write-Host "MOVED $relPath"
}

# --- Infrastructure: Configuration, Api, Hardware, Interfaces ---
Move-Tree "Configuration"
Move-Tree "Services\Api"
Move-Tree "Services\Hardware"
Move-Tree "Interfaces"

# --- Infrastructure: safe Models ---
$modelFiles = @(
    "Models\ApiDtos.cs",
    "Models\Auth\LoginRequest.cs",
    "Models\Auth\RefreshRequest.cs",
    "Models\BankQrSetting.cs",
    "Models\FilterCriteria.cs",
    "Models\Local\LocalUserRecord.cs",
    "Models\LocalProductRecord.cs",
    "Models\Pos\CartLineRow.cs",
    "Models\Pos\ReturnSaleLineVm.cs",
    "Models\Pos\ReturnSaleListItemVm.cs",
    "Models\Product.cs",
    "Models\RevisionLineVm.cs",
    "Models\ShiftHistoryEntry.cs",
    "Models\ShiftModel.cs",
    "Models\UpdateManifest.cs",
    "Models\WarehousePreset.cs"
)
foreach ($f in $modelFiles) {
    $src = Join-Path $wpf $f
    $dst = Join-Path $infra $f
    if (Test-Path $src) {
        $parent = Split-Path $dst -Parent
        if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        Move-Item $src $dst -Force
        Write-Host "MOVED $f"
    }
}

# --- Infrastructure: safe Services (wave 1) ---
$serviceFiles = @(
    "AnalyticsRecord.cs","ApiErrorParser.cs","ApiException.cs","AppVersionInfo.cs","AuthService.cs",
    "AutostartHelper.cs","CartInPlaceRecalculator.cs","CartJsonHelper.cs","CartReceiptTextBuilder.cs",
    "CartResponseHelper.cs","CartSaleSessionHelper.cs","CartSession.cs","CartTotalsCalculator.cs",
    "CartDisplayHelper.cs","CatalogBackgroundSyncService.cs","CatalogSyncResult.cs","CatalogVersionInfo.cs",
    "CatalogVersionParser.cs","CatalogViewMode.cs","CheckoutResponseHelper.cs","CheckoutValidation.cs",
    "CompanyInfoService.cs","DatabaseService.cs","DeferredCartEntry.cs","DeferredCartServerSync.cs",
    "DeferredCartsStore.cs","EscPosCommands.cs","EscPosTextReceiptPrinter.cs","GraphicReceiptGenerator.cs",
    "GraphicReceiptLayout.cs","GraphicReceiptPrinter.cs","HardwarePortHelper.cs","JsonNumericReader.cs",
    "MonospaceReceiptRenderer.cs","MySqlAuditService.cs","MySqlMonitorService.cs","NurMarketApiClient.cs",
    "OfflineAuthSessionStore.cs","OfflineDatabase.cs","OfflinePendingSalesStore.cs","OfflinePosStateStoreAdapter.cs",
    "OfflineSaleEntry.cs","OpenReceiptSnapshot.cs","OrderDiscountHelper.cs","PaymentErrorMessages.cs",
    "PosErrorMessages.cs","PosLogger.cs","PosRefundLineRequest.cs","PosRefundService.cs","PosSaleRowFormatter.cs",
    "PostgreSqlConnectionStringResolver.cs","PrinterPortService.cs","ProductImageUrl.cs","RawPrinterHelper.cs",
    "ReceiptEncodingHelper.cs","ReceiptLayout.cs","ReceiptPaperProfile.cs","ReceiptPdfPreviewService.cs",
    "ReceiptPrintService.cs","ReceiptSanitizer.cs","ReceiptTextFormatter.cs","ScaleReaderService.cs",
    "ScaleWeightParser.cs","ScaleWeightProvider.cs","ShiftBalanceHelper.cs","ShiftHelper.cs","ShiftService.cs",
    "StagingCartService.cs","StockAvailabilityService.cs","SyncService.cs","TestReceiptLineBuilder.cs",
    "UserPreferences.cs","WindowsDpapiHelper.cs"
)
$svcDst = Join-Path $infra "Services"
if (-not (Test-Path $svcDst)) { New-Item -ItemType Directory -Path $svcDst -Force | Out-Null }
foreach ($f in $serviceFiles) {
    $src = Join-Path $wpf "Services\$f"
    $dst = Join-Path $svcDst $f
    if (Test-Path $src) {
        Move-Item $src $dst -Force
        Write-Host "MOVED Services\$f"
    } else {
        Write-Host "SKIP Services\$f"
    }
}

# --- ViewModels ---
$vmSrc = Join-Path $wpf "ViewModels"
$vmDst = Join-Path $vms "ViewModels"
# Keep namespace NurMarketKassa.ViewModels — put files at project root of ViewModels project
if (-not (Test-Path $vms)) { New-Item -ItemType Directory -Path $vms -Force | Out-Null }
Get-ChildItem $vmSrc -Filter "*.cs" | ForEach-Object {
    Move-Item $_.FullName (Join-Path $vms $_.Name) -Force
    Write-Host "MOVED VM $($_.Name)"
}
if ((Test-Path $vmSrc) -and ((Get-ChildItem $vmSrc -Force | Measure-Object).Count -eq 0)) {
    Remove-Item $vmSrc -Force
}

# --- Assets placeholder ---
$assetsDir = Join-Path $assets "Assets"
if (-not (Test-Path $assetsDir)) { New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null }
$readme = Join-Path $assetsDir "README.txt"
if (-not (Test-Path $readme)) {
    Set-Content -Path $readme -Value "Place shared images here: app-logo.png, Login1.png, bank logos, fonts."
}

Write-Host "=== MIGRATION DONE ==="
Write-Host "Infra services count:" (Get-ChildItem $infra -Recurse -Filter "*.cs" | Measure-Object).Count
Write-Host "VM count:" (Get-ChildItem $vms -Filter "*.cs" | Measure-Object).Count
Write-Host "Remaining WPF Services:" (Get-ChildItem (Join-Path $wpf "Services") -Recurse -Filter "*.cs" -ErrorAction SilentlyContinue | Measure-Object).Count
