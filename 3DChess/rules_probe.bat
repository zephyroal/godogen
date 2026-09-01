@echo off
chcp 65001 >nul
echo === 3DChess 规则探针 ===
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --headless --path "D:\godogen\3DChess" --script test/RulesProbe.cs
pause
