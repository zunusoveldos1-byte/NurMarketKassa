$ErrorActionPreference = "Stop"
$ava = "C:\Users\User\Desktop\NurCrmPosKassa-master\NurCrmPosKassa-master\src\NurMarketKassa.Avalonia"

# Fix resource paths
$files = Get-ChildItem $ava -Recurse -Include "*.axaml","*.cs" -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
foreach ($f in $files) {
  $t = [System.IO.File]::ReadAllText($f.FullName)
  $n = $t
  $n = $n.Replace("Views.axaml/Main/Styles/MainViewStyles.axaml", "Views.axaml/Shared/MainViewStyles.axaml")
  $n = $n.Replace("Views.axaml/ShiftHistoryResources.axaml", "Views.axaml/Shared/ShiftHistoryResources.axaml")
  if ($n -ne $t) {
    [System.IO.File]::WriteAllText($f.FullName, $n, [System.Text.UTF8Encoding]::new($false))
    Write-Host "PATH $($f.FullName)"
  }
}

# Delete all Thin.cs and leftover bak/wpfbak in Avalonia
$deleted = 0
Get-ChildItem $ava -Recurse -File | Where-Object {
  $_.Name -like "*.Thin.cs" -or $_.Name -like "*.bak" -or $_.Name -like "*.wpfbak" -or $_.Name -like "*.shellbak"
} | ForEach-Object {
  Remove-Item $_.FullName -Force
  $deleted++
  Write-Host "DEL $($_.FullName)"
}
Write-Host "Deleted $deleted thin/bak files"

# Ensure Assets placeholder
$assetsReadme = Join-Path $ava "Assets\README.txt"
if (-not (Test-Path $assetsReadme)) {
  [System.IO.File]::WriteAllText($assetsReadme, "App assets are linked from NurMarketKassa/Assets and NurMarketKassa.Assets at build time.`n", [System.Text.UTF8Encoding]::new($false))
}

Write-Host "DONE"