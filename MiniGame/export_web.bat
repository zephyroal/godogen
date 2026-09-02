@echo off
chcp 65001 >nul
echo === Exporting Web (HTML5) build ===

echo Step 1: Build C#
dotnet build --nologo -v q
if %errorlevel% neq 0 (echo BUILD FAILED & pause & exit /b 1)

echo Step 2: Ensure export preset exists
if not exist export_presets.cfg (
  echo ERROR: No export preset found.
  echo Please open Godot Editor and add a Web export preset first:
  echo   Project -^> Export -^> Add -^> Web (HTML5)
  pause
  exit /b 1
)

echo Step 3: Export
set VK_ICD_FILENAMES=C:\WINDOWS\System32\DriverStore\FileRepository\nv_dispsi.inf_amd64_bd43d31db3bd09e9\nv-vk64.json
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --headless --export-release "Web" export/web/index.html

if %errorlevel% neq 0 (
  echo EXPORT FAILED - check that Web export templates are installed:
  echo   Editor -^> Manage Export Templates -^> Download Web template
  pause
  exit /b 1
)

echo === Done: export/web/index.html ===
echo Preview: cd export\web ^& python -m http.server 8080
pause
