@echo off
chcp 65001 >nul
echo === 编译 3DChess ===
cd /d "D:\godogen\3DChess"
dotnet build --nologo -v q
if %errorlevel% neq 0 (
    echo 编译失败！
    pause
    exit /b 1
)
echo === 编译成功 ===
pause
