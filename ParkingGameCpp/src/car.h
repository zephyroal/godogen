#pragma once

#include <godot_cpp/classes/vehicle_body3d.hpp>
#include <godot_cpp/classes/vehicle_wheel3d.hpp>
#include <godot_cpp/classes/node3d.hpp>
#include <godot_cpp/classes/standard_material3d.hpp>

#include <vector>

namespace godot {

/// <summary>The player car — C++ GDExtension twin of the C# Car: VehicleBody3D
/// physics (RWD, manual R/N/D gears, speed-sensitive steering, brake lights).
/// Sign note (verified on 4.7.1): body-level EngineForce positive pushes
/// toward +Z (the tail), so forward (-Z) drive uses negative values.</summary>
class CppCar : public VehicleBody3D {
    GDCLASS(CppCar, VehicleBody3D)

public:
    enum Gear { GEAR_N, GEAR_D, GEAR_R };

    // control inputs, written externally each tick (keyboard or the demo)
    float throttle = 0.0f;
    float brake_input = 0.0f;
    float steer_input = 0.0f;
    bool handbrake = false;
    Gear current_gear = GEAR_N;

    static constexpr float SEDAN_LEN = 4.6f;
    static constexpr float SEDAN_WID = 1.8f;

    float get_forward_speed() const;
    void select_gear(Gear g) { current_gear = g; }
    Gear get_gear() const { return current_gear; }

    void set_brake_lights(bool on);
    std::vector<Vector3> corners() const;

    void _ready() override;
    void _physics_process(double delta) override;

protected:
    static void _bind_methods() {}

private:
    VehicleWheel3D *_fl = nullptr, *_fr = nullptr;
    VehicleWheel3D *_rl = nullptr, *_rr = nullptr;
    Node3D *_fl_pivot = nullptr, *_fr_pivot = nullptr;
    float _steer = 0.0f;
    Ref<StandardMaterial3D> _tail;

    void build_visuals();
    VehicleWheel3D *make_wheel(const Vector3 &pos, bool steering, bool traction);
};

} // namespace godot
