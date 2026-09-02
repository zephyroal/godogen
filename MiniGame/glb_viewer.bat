@echo off
chcp 65001 >nul
echo === GLB 交互式查看器 (MiniGame) ===
echo 拖入 .glb 文件到窗口即可加载
echo 鼠标拖动旋转 · 滚轮缩放 · Space 下一个 · F1 线框 · F2 AABB · R 重置 · ESC 退出
echo.
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path "D:\godogen\MiniGame" --script test/GlbViewer.cs
