@echo off
REM Builds and runs ParkingGame in a window.
setlocal
cd /d D:\godogen\ParkingGame
dotnet build --nologo -v q
if errorlevel 1 ( echo build failed & exit /b 1 )
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path D:\godogen\ParkingGame
