@echo off
chcp 65001 >nul
cd /d D:\godogen\ParkingGame
REM 自动演示第 1 关倒车入库（用于无输入注入的端到端验证），结果写入 demo_state.txt
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path D:\godogen\ParkingGame -- --demo
