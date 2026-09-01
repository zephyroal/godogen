@echo off
chcp 65001 >nul
echo === Building C# ===
dotnet build --nologo -v q
if %errorlevel% neq 0 (echo BUILD FAILED & pause & exit /b 1)

echo === Recording 90s proof video ===
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --write-movie screenshots/result/frame.png --fixed-fps 30 --quit-after 2700 --script test/Presentation.cs

echo === Encoding video.mp4 ===
ffmpeg -y -loglevel error -framerate 30 -start_number 0 -i "screenshots/result/frame%%08d.png" -frames:v 2700 -c:v libx264 -pix_fmt yuv420p -movflags +faststart screenshots/result/video.mp4

echo === Done: screenshots/result/video.mp4 ===
pause
