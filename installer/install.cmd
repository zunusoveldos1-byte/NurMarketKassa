@echo off
setlocal

set "APP_NAME=NurMarketKassa"
set "TARGET=%LocalAppData%\Programs\%APP_NAME%"
set "SRC=%~dp0"

if not exist "%TARGET%" mkdir "%TARGET%"

robocopy "%SRC%" "%TARGET%" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /XF install.cmd >nul
set "RC=%ERRORLEVEL%"
if %RC% GEQ 8 (
    echo Failed to copy application files.
    exit /b %RC%
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "$ws=New-Object -ComObject WScript.Shell; " ^
 "$desktop=[Environment]::GetFolderPath('Desktop'); " ^
 "$startMenu=[Environment]::GetFolderPath('Programs'); " ^
 "$target=Join-Path $env:LOCALAPPDATA 'Programs\NurMarketKassa\NurMarketKassa.exe'; " ^
 "$workdir=Split-Path $target; " ^
 "$desktopLink=$ws.CreateShortcut((Join-Path $desktop 'Nur Market Kassa.lnk')); " ^
 "$desktopLink.TargetPath=$target; $desktopLink.WorkingDirectory=$workdir; $desktopLink.Save(); " ^
 "$menuDir=Join-Path $startMenu 'Nur Market'; if(-not (Test-Path $menuDir)){ New-Item -ItemType Directory -Path $menuDir | Out-Null }; " ^
 "$menuLink=$ws.CreateShortcut((Join-Path $menuDir 'Nur Market Kassa.lnk')); " ^
 "$menuLink.TargetPath=$target; $menuLink.WorkingDirectory=$workdir; $menuLink.Save();"

start "" "%TARGET%\NurMarketKassa.exe"
exit /b 0
