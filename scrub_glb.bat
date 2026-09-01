@echo off
chcp 65001 >nul
echo === Scrub vendor metadata from all project GLB/JPEG assets ===
set SCRUB=D:\godogen\scrub_glb.py

echo.
echo --- MiniGame GLB ---
python "%SCRUB%" "D:\godogen\MiniGame\assets\glb"

echo.
echo --- 3DChess concept JPEG ---
python "%SCRUB%" "D:\godogen\3DChess\3DModel\concepts"

echo.
echo --- 3DChess GLB ---
python "%SCRUB%" "D:\godogen\3DChess\assets\glb"

echo.
echo === Done ===
pause
