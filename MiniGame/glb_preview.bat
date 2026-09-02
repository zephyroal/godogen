@echo off
chcp 65001 >nul
echo === GLB 模型预览 (MiniGame) ===
echo 用法: glb_preview.bat [目录] [--spin] [--height=N]
echo 不带参数则扫描 assets\glb
echo --spin 慢速旋转模式（录屏用）
echo --height=6 统一设置显示高度
echo.
set VK_ICD_FILENAMES=C:\WINDOWS\System32\DriverStore\FileRepository\nv_dispsi.inf_amd64_bd43d31db3bd09e9\nv-vk64.json
set GODOT=D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe
if "%~1"=="" (
    "%GODOT%" --path "D:\godogen\MiniGame" --script test/GlbPreview.cs
) else (
    "%GODOT%" --path "D:\godogen\MiniGame" --script test/GlbPreview.cs -- %1 %2 %3
)
echo === 完成 ===
echo 截图: screenshots\preview_*.png
echo 报告: screenshots\glb_report.json
pause
