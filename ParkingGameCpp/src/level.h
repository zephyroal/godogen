#pragma once

#include <godot_cpp/classes/node3d.hpp>

namespace godot {

/// <summary>Builds level 1 (侧方入库) procedurally: floor, painted slot, road
/// markings, walls, parked cars. Static geometry only — the C++ version
/// ships the core loop; hazards/weather/editor live in the C# reference
/// implementation.</summary>
class CppLevel : public Node3D {
    GDCLASS(CppLevel, Node3D)

public:
    // level-1 definition (kept explicit — mirrors LevelDef.L1 in the C# twin)
    static constexpr float SLOT_X = 6.5f;
    static constexpr float SLOT_Z = -2.6f;
    static constexpr float SLOT_LEN = 6.0f;
    static constexpr float SLOT_WID = 2.5f;
    static constexpr float ANGLE_TOL_DEG = 15.0f;

    void build_level1();

protected:
    static void _bind_methods() {}

private:
    void add_static_box(const Vector3 &center, const Vector3 &size,
                        const Color &color, const String &name);
    void paint_slot();
    void paint_road();
    void add_parked_car(const Vector3 &pos, float yaw_deg, const Color &color);
};

} // namespace godot
