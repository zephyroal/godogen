@echo off
chcp 65001 >nul
echo === 清洗全部项目 GLB/JPEG 厂商元数据 ===
set SCRUB=D:\godogen\scrub_glb.py

echo.
echo --- MiniGame GLB ---
python "%SCRUB%" "D:\godogen\MiniGame\assets\glb"

echo.
echo --- 3DChess 概念图 JPEG ---
python "%SCRUB%" "D:\godogen\3DChess\3DModel\concepts"

echo.
echo --- 3DChess GLB (if any) ---
if exist "D:\godogen\3DChess\3DModel\glb" python "%SCRUB%" "D:\godogen\3DChess\3DModel\glb"

echo.
echo === 清洗完成 ===
pause
