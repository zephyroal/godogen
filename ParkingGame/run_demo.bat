@echo off
REM Runs the level-1 autopilot demo end to end (no input injection needed).
REM Telemetry lands in demo_state.txt; exit code 0 = parked successfully.
setlocal
cd /d D:\godogen\ParkingGame
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path D:\godogen\ParkingGame -- --demo
