@echo off
chcp 65001 >nul
echo === 录制 3DChess 证明视频 ===
set GODOT=D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe
set OUTDIR=D:\godogen\3DChess\screenshots\result

if not exist "%OUTDIR%" mkdir "%OUTDIR%"

echo 正在录制 30s 视频 (900 帧 @ 30fps)...
"%GODOT%" --path "D:\godogen\3DChess" --write-movie "%OUTDIR%\frame.png" --fixed-fps 30 --quit-after 900 --script test/Presentation.cs
if %errorlevel% neq 0 (
    echo 录制失败！
    pause
    exit /b 1
)
echo === 录制完成 ===
echo 帧文件: %OUTDIR%\frame0000000*.png
echo 用 ffmpeg 合成视频:
echo   ffmpeg -y -framerate 30 -start_number 0 -i "%OUTDIR%\frame%%08d.png" -frames:v 900 -c:v libx264 -pix_fmt yuv420p -movflags +faststart "%OUTDIR%\video.mp4"
pause
