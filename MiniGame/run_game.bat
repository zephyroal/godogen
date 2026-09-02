@echo off
chcp 65001 >nul
echo === Building C# ===
dotnet build --nologo -v q
if %errorlevel% neq 0 (echo BUILD FAILED & pause & exit /b 1)
echo === Running game (Vulkan) ===
set VK_ICD_FILENAMES=C:\WINDOWS\System32\DriverStore\FileRepository\nv_dispsi.inf_amd64_bd43d31db3bd09e9\nv-vk64.json
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" .
