@echo off
REM Records the --demo autopilot park as a deterministic 30fps PNG sequence
REM with Godot's movie writer, then stitches it into parking_demo.mp4 (ffmpeg).
REM ASCII only on purpose: "chcp 65001" inside a .bat with non-ASCII comments
REM desyncs cmd.exe's line reader and mangles later lines (bare Godot launch).
setlocal
cd /d D:\godogen\ParkingGame
set GODOT=D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe
if not exist screenshots\movie mkdir screenshots\movie
del /q screenshots\movie\* >nul 2>&1
"%GODOT%" --path D:\godogen\ParkingGame --write-movie screenshots\movie\frame.png --fixed-fps 30 -- --demo
if errorlevel 1 ( echo capture failed & exit /b 1 )
ffmpeg -y -framerate 30 -i screenshots\movie\frame%%08d.png -c:v libx264 -pix_fmt yuv420p -movflags +faststart screenshots\movie\parking_demo.mp4
echo done: screenshots\movie\parking_demo.mp4
