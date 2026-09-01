@echo off
chcp 65001 >nul
echo === Building C# ===
dotnet build --nologo -v q
if %errorlevel% neq 0 (echo BUILD FAILED & pause & exit /b 1)
echo === Running game ===
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" .
