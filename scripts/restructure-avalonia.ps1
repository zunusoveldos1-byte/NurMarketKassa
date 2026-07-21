#requires -Version 5.1
$ErrorActionPreference = "Stop"
$root = "C:\Users\User\Desktop\NurCrmPosKassa-master\NurCrmPosKassa-master"
$ava = Join-Path $root "src\NurMarketKassa.Avalonia"
$views = Join-Path $ava "Views.axaml"

function Ensure-Dir([string]$p) {
  if (-not (Test-Path $p)) { New-Item -ItemType Directory -Path $p -Force | Out-Null }
}
function Move-Safe([string]$src, [string]$dst) {
  if (-not (Test-Path $src)) { return }
  Ensure-Dir (Split-Path $dst -Parent)
  if (Test-Path $dst) { Remove-Item $dst -Force -Recurse }
  Move-Item -LiteralPath $src -Destination $dst -Force
  Write-Host "MOVE $src -> $dst"
}

# --- 1) Solution: only Avalonia ---
$sln = @"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "NurMarketKassa.Avalonia", "src\NurMarketKassa.Avalonia\NurMarketKassa.Avalonia.csproj", "{7D0109C0-AA3E-419D-92FD-990EE40E08B9}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{7D0109C0-AA3E-419D-92FD-990EE40E08B9}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{7D0109C0-AA3E-419D-92FD-990EE40E08B9}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{7D0109C0-AA3E-419D-92FD-990EE40E08B9}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{7D0109C0-AA3E-419D-92FD-990EE40E08B9}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
	GlobalSection(ExtensibilityGlobals) = postSolution
		SolutionGuid = {8D8D6D26-C359-4BDE-BC19-2D27CA58D846}
	EndGlobalSection
EndGlobal
"@
[System.IO.File]::WriteAllText((Join-Path $root "NurMarketKassa.sln"), $sln, [System.Text.UTF8Encoding]::new($false))
Write-Host "SLN rewritten"

# --- 2) Folder structure ---
Ensure-Dir (Join-Path $views "Login")
Ensure-Dir (Join-Path $views "Main")
Ensure-Dir (Join-Path $views "Dialogs")
Ensure-Dir (Join-Path $views "Controls")
Ensure-Dir (Join-Path $views "Shared")
Ensure-Dir (Join-Path $ava "Assets")

# Login
Move-Safe (Join-Path $views "LoginWindow.axaml") (Join-Path $views "Login\LoginWindow.axaml")
Move-Safe (Join-Path $views "LoginWindow.axaml.cs") (Join-Path $views "Login\LoginWindow.axaml.cs")
Move-Safe (Join-Path $views "LoginView.axaml") (Join-Path $views "Login\LoginView.axaml")
Move-Safe (Join-Path $views "LoginView.axaml.cs") (Join-Path $views "Login\LoginView.axaml.cs")

# Main windows
$mainWindows = @(
  "MainWindow","AdminSupportWindow","FilterWindow","FinanceWindow","PosSettingsWindow",
  "SalesWindow","ServicesWindow","WarehouseWindow","ShiftsHistoryWindow",
  "ShiftHistoryView","ShiftSummaryView"
)
foreach ($n in $mainWindows) {
  Move-Safe (Join-Path $views "$n.axaml") (Join-Path $views "Main\$n.axaml")
  Move-Safe (Join-Path $views "$n.axaml.cs") (Join-Path $views "Main\$n.axaml.cs")
}

# Controls: move Main\Controls\* up to Controls\
$oldControls = Join-Path $views "Main\Controls"
if (Test-Path $oldControls) {
  Get-ChildItem $oldControls -File | ForEach-Object {
    Move-Safe $_.FullName (Join-Path $views "Controls\$($_.Name)")
  }
  Remove-Item $oldControls -Recurse -Force -ErrorAction SilentlyContinue
}

# Shared resources / styles
Move-Safe (Join-Path $views "ShiftHistoryResources.axaml") (Join-Path $views "Shared\ShiftHistoryResources.axaml")
$mainStyles = Join-Path $views "Main\Styles"
if (Test-Path $mainStyles) {
  Get-ChildItem $mainStyles -File | ForEach-Object {
    Move-Safe $_.FullName (Join-Path $views "Shared\$($_.Name)")
  }
  Remove-Item $mainStyles -Recurse -Force -ErrorAction SilentlyContinue
}

# Dialog theme/styles stay in Dialogs (they're dialog UI), also copy PosDialog* helpers stay

# Remove backups
Get-ChildItem $views -Recurse -Include "*.bak","*.wpfbak","*.shellbak","*_wpf_port_backup*" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path (Join-Path $views "_wpf_port_backup")) { Remove-Item (Join-Path $views "_wpf_port_backup") -Recurse -Force }

Write-Host "Folders restructured"