@echo off
chcp 65001 >nul
cd /d D:\godogen\ParkingGame
echo === 编译 + 生成场景 ===
dotnet build --nologo -v q
if %errorlevel% neq 0 ( echo 编译失败 & exit /b 1 )
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --headless --path D:\godogen\ParkingGame --import 2>&1 | findstr /i "error" 
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --headless --path D:\godogen\ParkingGame --script scenes/BuildMain.cs
echo === 完成 ===
