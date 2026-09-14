# CocosChess — 2D Xiangqi on Cocos2d-x 4 (CMake, matching the official cpp-template pattern)
cmake_minimum_required(VERSION 3.18)
project(CocosChess)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(COCOS2DX_ROOT "" CACHE PATH "Path to the cocos2d-x v4 source root")
if(NOT COCOS2DX_ROOT)
    message(FATAL_ERROR "Pass -DCOCOS2DX_ROOT=/path/to/cocos2d-x to CMake.")
endif()
if(NOT EXISTS "${COCOS2DX_ROOT}/cocos/CMakeLists.txt")
    message(FATAL_ERROR "COCOS2DX_ROOT='${COCOS2DX_ROOT}' does not look like a cocos2d-x checkout (cocos/CMakeLists.txt not found). Run python download-deps.py inside it first.")
endif()

set(COCOS2DX_ROOT_PATH ${COCOS2DX_ROOT})
set(CMAKE_MODULE_PATH ${COCOS2DX_ROOT_PATH}/cmake/Modules/)
include(CocosBuildSet)
add_subdirectory(${COCOS2DX_ROOT_PATH}/cocos ${ENGINE_BINARY_PATH}/cocos/core)

# game sources
set(GAME_SOURCE
    Classes/AppDelegate.cpp
    Classes/GameScene.cpp
    Classes/NetPlayer.cpp
    Classes/main.cpp
)
set(GAME_HEADER
    Classes/AppDelegate.h
    Classes/ChessRules.h
    Classes/AIPlayer.h
    Classes/GameScene.h
    Classes/NetPlayer.h
    Classes/main.h
)
set(GAME_RES_FOLDER "${CMAKE_CURRENT_SOURCE_DIR}/Resources")

if(WINDOWS)
    cocos_mark_multi_resources(common_res_files RES_TO "Resources" FOLDERS ${GAME_RES_FOLDER})
    list(APPEND GAME_SOURCE ${common_res_files})
endif()

add_executable(${PROJECT_NAME} WIN32
    ${GAME_HEADER}
    ${GAME_SOURCE}
)

target_include_directories(${PROJECT_NAME} PRIVATE
    Classes
    ${COCOS2DX_ROOT_PATH}/cocos/cocos
    ${COCOS2DX_ROOT_PATH}/cocos/audio/include/
)
target_link_libraries(${PROJECT_NAME} cocos2d)

# MSVC: source contains UTF-8 Chinese string literals (棋子汉字) — requires /utf-8
if(MSVC)
    target_compile_options(${PROJECT_NAME} PRIVATE /W3 /utf-8)
else()
    target_compile_options(${PROJECT_NAME} PRIVATE -Wall -Wextra)
endif()

# engine helpers: copy DLLs + Resources next to the exe
setup_cocos_app_config(${PROJECT_NAME})
if(WINDOWS)
    cocos_copy_target_dll(${PROJECT_NAME})
endif()
if(LINUX OR WINDOWS)
    cocos_get_resource_path(APP_RES_DIR ${PROJECT_NAME})
    cocos_copy_target_res(${PROJECT_NAME} LINK_TO ${APP_RES_DIR} FOLDERS ${GAME_RES_FOLDER})
endif()
