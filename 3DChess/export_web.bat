@echo off
chcp 65001 >nul
echo === 导出 3DChess Web 版 ===
echo 需要先在编辑器中配置 Web 导出预设 (Project ^> Export ^> Web)
echo.
set GODOT=D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe
set OUTDIR=D:\godogen\3DChess\export\web

if not exist "%OUTDIR%" mkdir "%OUTDIR%"

"%GODOT%" --headless --path "D:\godogen\3DChess" --export-release "Web" "%OUTDIR%\index.html"
if %errorlevel% neq 0 (
    echo 导出失败！请确认已安装 Web 导出模板并配置 export_presets.cfg
    pause
    exit /b 1
)
echo === 导出完成: %OUTDIR%\index.html ===
pause
