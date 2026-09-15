@echo off
REM Builds the godot-cpp bindings (once) and then this GDExtension.
REM Both target the exact Godot 4.7.1 API via the editor-dumped extension_api.json.
REM ASCII only on purpose: chcp 65001 + non-ASCII comments desync cmd.exe.
setlocal
set API=D:\godogen\godot_api471\extension_api.json
set GDCPP=D:\godogen\godot-cpp

if not exist "%GDCPP%\bin\libgodot-cpp.windows.template_debug.x86_64.lib" (
    echo === building godot-cpp bindings ===
    python -m SCons -C "%GDCPP%" platform=windows target=template_debug arch=x86_64 custom_api_file=%API% -j10
    if errorlevel 1 ( echo godot-cpp build failed & exit /b 1 )
)

echo === building ParkingGameCpp extension ===
python -m SCons platform=windows target=template_debug arch=x86_64 custom_api_file=%API% -j10
if errorlevel 1 ( echo extension build failed & exit /b 1 )
echo done: bin\libparking_game_cpp.windows.template_debug.x86_64.dll
