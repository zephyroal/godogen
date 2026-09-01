@echo off
chcp 65001 >nul
echo === Sim Probe (AI vs AI, no rendering) ===
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --headless --script test/SimProbe.cs
pause
