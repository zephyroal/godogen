@echo off
chcp 65001 >nul
echo === 编译 + 运行 3DChess ===
cd /d "D:\godogen\3DChess"
dotnet build --nologo -v q
if %errorlevel% neq 0 (
    echo 编译失败！
    pause
    exit /b 1
)
echo === 运行中... ===
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path "D:\godogen\3DChess"
