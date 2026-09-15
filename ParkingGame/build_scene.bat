@echo off
REM Builds, imports assets headlessly, then regenerates scenes/Main.tscn.
setlocal
cd /d D:\godogen\ParkingGame
echo === build + generate scene ===
dotnet build --nologo -v q
if errorlevel 1 ( echo build failed & exit /b 1 )
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --headless --path D:\godogen\ParkingGame --import 2>&1 | findstr /i "error"
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --headless --path D:\godogen\ParkingGame --script scenes/BuildMain.cs
echo === done ===
