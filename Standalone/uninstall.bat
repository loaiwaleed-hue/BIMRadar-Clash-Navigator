@echo off
setlocal EnableDelayedExpansion
rem BIMRadar Clash Navigator - standalone uninstaller. Removes only what install.bat created.
set "ADDINS=%AppData%\Autodesk\Revit\Addins"
set "COUNT=0"

for %%Y in (2022 2023 2024 2025 2026 2027) do (
  set "FOUND=0"
  if exist "%ADDINS%\%%Y\BIMRadar.ClashNavigator.addin" (
    del /Q "%ADDINS%\%%Y\BIMRadar.ClashNavigator.addin"
    set "FOUND=1"
  )
  if exist "%ADDINS%\%%Y\BIMRadar.ClashNavigator" (
    rmdir /S /Q "%ADDINS%\%%Y\BIMRadar.ClashNavigator"
    set "FOUND=1"
  )
  if "!FOUND!"=="1" (
    echo Removed from Revit %%Y
    set /a COUNT+=1
  )
)

echo.
if "!COUNT!"=="0" (
  echo Nothing to remove.
) else (
  echo Removed from !COUNT! Revit version^(s^). Restart Revit.
)
echo Settings in %AppData%\BIMRadar\ClashNavigator were left untouched.
pause
