@echo off
setlocal EnableDelayedExpansion
rem BIMRadar Clash Navigator - standalone installer (per user, per Revit year 2022-2027).
set "SRC=%~dp0"
set "ADDINS=%AppData%\Autodesk\Revit\Addins"
set "PYCFG=%AppData%\pyRevit\pyRevit_config.ini"
set "COUNT=0"
set "LIST="

for %%Y in (2022 2023 2024 2025 2026 2027) do (
  if exist "%ADDINS%\%%Y" (
    set "TFM=net48"
    if %%Y GEQ 2025 set "TFM=net8"
    if %%Y GEQ 2027 set "TFM=net10"
    if not exist "%SRC%!TFM!\BIMRadar.ClashNavigator.dll" (
      echo Missing "%SRC%!TFM!\BIMRadar.ClashNavigator.dll" - skipping Revit %%Y
    ) else (
      if not exist "%ADDINS%\%%Y\BIMRadar.ClashNavigator" mkdir "%ADDINS%\%%Y\BIMRadar.ClashNavigator"
      copy /Y "%SRC%BIMRadar.ClashNavigator.addin" "%ADDINS%\%%Y\" >nul
      copy /Y "%SRC%!TFM!\BIMRadar.ClashNavigator.dll" "%ADDINS%\%%Y\BIMRadar.ClashNavigator\" >nul
      if exist "%SRC%!TFM!\BIMRadar.ClashNavigator.pdb" copy /Y "%SRC%!TFM!\BIMRadar.ClashNavigator.pdb" "%ADDINS%\%%Y\BIMRadar.ClashNavigator\" >nul
      powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%ADDINS%\%%Y\BIMRadar.ClashNavigator' -Recurse | Unblock-File; Unblock-File -LiteralPath '%ADDINS%\%%Y\BIMRadar.ClashNavigator.addin'" >nul 2>&1
      echo Installed for Revit %%Y ^(!TFM!^)
      set /a COUNT+=1
      set "LIST=!LIST! %%Y"
    )
  )
)

echo.
if exist "%PYCFG%" (
  findstr /i /r /c:"^userextensions.*BIMRadar" "%PYCFG%" >nul 2>&1
  if not errorlevel 1 (
    echo WARNING: pyRevit loads a BIMRadar folder ^(Settings ^> Custom Extension Directories^).
    echo If it holds the Clash Viewer extension, this add-in stays hidden to avoid two Review panels.
    echo.
  )
)

echo ---- Summary ----
if "!COUNT!"=="0" (
  echo No Revit 2022-2027 Addins folder found under !ADDINS!. Nothing installed.
) else (
  echo Installed for !COUNT! Revit version^(s^):!LIST!
  echo Restart Revit.
)
pause
