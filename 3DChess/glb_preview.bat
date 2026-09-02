@echo off
chcp 65001 >nul
echo === GLB 模型预览 (3DChess) ===
echo 用法: glb_preview.bat [目录] [--spin]
echo 不带参数则扫描 3DModel\glb 或 assets\glb
echo --spin 慢速旋转模式（录屏用）
echo.
set GODOT=D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe
if "%~1"=="" (
    "%GODOT%" --path "D:\godogen\3DChess" --script test/GlbPreview.cs
) else (
    "%GODOT%" --path "D:\godogen\3DChess" --script test/GlbPreview.cs -- --dir %1 %2 %3
)
echo === 完成 ===
echo 截图: screenshots\preview_*.png
echo 报告: screenshots\glb_report.json
pause
