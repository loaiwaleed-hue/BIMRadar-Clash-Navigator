@echo off
setlocal EnableDelayedExpansion
rem BIMRadar Clash Navigator - provisional installer (per-user, per Revit year).
set "SRC=%~dp0"
set "ADDINS=%AppData%\Autodesk\Revit\Addins"
set "COUNT=0"

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
      echo Installed for Revit %%Y ^(!TFM!^) into %ADDINS%\%%Y
      set /a COUNT+=1
    )
  )
)

echo.
if "!COUNT!"=="0" (
  echo No Revit 2022-2027 Addins folder found under %ADDINS%. Nothing installed.
) else (
  echo Installed for !COUNT! Revit version^(s^). Restart Revit.
)
pause
