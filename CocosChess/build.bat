@echo off
rem Build CocosChess against a local cocos2d-x v4 checkout.
rem NOTE: cocos2d-x v4 prebuilt third-party libs are Win32-only — use -A Win32.
rem Usage: build.bat D:\path\to\cocos2d-x
setlocal
chcp 65001 >nul

if "%~1"=="" (
    echo Usage: build.bat ^<path-to-cocos2d-x^>
    echo   e.g. build.bat D:\godogen\cocos2d-x-parent\cocos2d-x
    exit /b 1
)
set COCOS2DX_ROOT=%~1

where cmake >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: cmake not found in PATH. Install CMake or use the VS Developer Command Prompt.
    exit /b 1
)

echo === Configuring (Win32 — v4 prebuilt libs are 32-bit) ===
cmake -B build -DCOCOS2DX_ROOT="%COCOS2DX_ROOT%" -A Win32
if %errorlevel% neq 0 (echo CONFIGURE FAILED & exit /b 1)

echo === Building (Release) ===
cmake --build build --config Release
if %errorlevel% neq 0 (echo BUILD FAILED & exit /b 1)

echo === Done ===
for /f "delims=" %%i in ('dir /b /s build\bin\Release\CocosChess.exe 2^>nul') do (
    echo Executable: %%i
)
