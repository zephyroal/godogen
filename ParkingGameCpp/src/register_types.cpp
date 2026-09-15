#include "register_types.h"
#include "car.h"
#include "game.h"
#include "level.h"

#include <gdextension_interface.h>

#include <godot_cpp/core/defs.hpp>
#include <godot_cpp/godot.hpp>

using namespace godot;

void initialize_parking_game_cpp_module(godot::ModuleInitializationLevel p_level) {
    if (p_level != godot::MODULE_INITIALIZATION_LEVEL_SCENE) {
        return;
    }
    GDREGISTER_CLASS(CppCar);
    GDREGISTER_CLASS(CppLevel);
    GDREGISTER_CLASS(CppGame);
}

void uninitialize_parking_game_cpp_module(godot::ModuleInitializationLevel p_level) {
    if (p_level != godot::MODULE_INITIALIZATION_LEVEL_SCENE) {
        return;
    }
}

extern "C" {
GDExtensionBool GDE_EXPORT parking_game_cpp_entry(
        GDExtensionInterfaceGetProcAddress p_get_proc_address,
        const GDExtensionClassLibraryPtr p_library,
        GDExtensionInitialization *r_initialization) {
    ::godot::GDExtensionBinding::InitObject init_obj(p_get_proc_address, p_library,
                                                     r_initialization);
    init_obj.register_initializer(&initialize_parking_game_cpp_module);
    init_obj.register_terminator(&uninitialize_parking_game_cpp_module);
    init_obj.set_minimum_library_initialization_level(
        godot::MODULE_INITIALIZATION_LEVEL_SCENE);
    return init_obj.init();
}
}
