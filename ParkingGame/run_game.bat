@echo off
chcp 65001 >nul
cd /d D:\godogen\ParkingGame
dotnet build --nologo -v q
if %errorlevel% neq 0 ( echo 编译失败 & exit /b 1 )
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path D:\godogen\ParkingGame
