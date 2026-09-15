@echo off
REM Runs the C++ GDExtension game; append -- --demo for the autopilot
REM verification run (RESULT SUCCESS in demo_state.txt, exit code 0).
REM ASCII only on purpose: chcp 65001 + non-ASCII comments desync cmd.exe.
setlocal
cd /d D:\godogen\ParkingGameCpp
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --headless --path D:\godogen\ParkingGameCpp --import
if errorlevel 1 ( echo import failed & exit /b 1 )
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path D:\godogen\ParkingGameCpp %* -- %*
