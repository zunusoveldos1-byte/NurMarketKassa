$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$src = Join-Path $PSScriptRoot "..\src\NurMarketKassa\Assets\app-logo.png"
$dst = Join-Path $PSScriptRoot "..\src\NurMarketKassa\Assets\app-logo.ico"
$b = [System.Drawing.Bitmap]::FromFile((Resolve-Path $src))
$h = $b.GetHicon()
$ic = [System.Drawing.Icon]::FromHandle($h)
try {
    $fs = [System.IO.File]::Create($dst)
    $ic.Save($fs)
    $fs.Close()
}
finally {
    $ic.Dispose()
    $b.Dispose()
}
Write-Host "Wrote $dst"
