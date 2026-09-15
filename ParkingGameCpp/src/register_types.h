#pragma once

#include <godot_cpp/core/class_db.hpp>

// global scope on purpose — the GDExtensionBinding callback signature in the
// current godot-cpp master expects plain ModuleInitializationLevel functions

void initialize_parking_game_cpp_module(godot::ModuleInitializationLevel p_level);
void uninitialize_parking_game_cpp_module(godot::ModuleInitializationLevel p_level);
