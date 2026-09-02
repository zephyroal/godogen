@echo off
chcp 65001 >nul
echo === Opening Godot Editor (Vulkan) ===
set VK_ICD_FILENAMES=C:\WINDOWS\System32\DriverStore\FileRepository\nv_dispsi.inf_amd64_bd43d31db3bd09e9\nv-vk64.json
"D:\godogen\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe" --editor --path .
