#pragma once

#include "car.h"
#include "level.h"

#include <godot_cpp/classes/button.hpp>
#include <godot_cpp/classes/camera3d.hpp>
#include <godot_cpp/classes/canvas_layer.hpp>
#include <godot_cpp/classes/color_rect.hpp>
#include <godot_cpp/classes/control.hpp>
#include <godot_cpp/classes/input_event.hpp>
#include <godot_cpp/classes/label.hpp>
#include <godot_cpp/classes/node3d.hpp>

namespace godot {

/// <summary>C++ GDExtension twin of the C# Game: environment, follow camera,
/// HUD-lite, keyboard input, the "报告我停好了" verdict, and the full demo
/// autopilot (same kinematic constants as the C# reference) for headless
/// verification with demo_state.txt telemetry.</summary>
class CppGame : public Node3D {
    GDCLASS(CppGame, Node3D)

public:
    void _ready() override;
    void _input(const Ref<InputEvent> &event) override;
    void _physics_process(double delta) override;
    void _process(double delta) override;

    /// Callable target for the HUD report button.
    void report_parked();

protected:
    static void _bind_methods();

private:
    CppCar *_car = nullptr;
    CppLevel *_level = nullptr;
    Camera3D *_cam = nullptr;
    CanvasLayer *_hud = nullptr;
    Control *_start_overlay = nullptr;
    Control *_end_overlay = nullptr;
    Label *_prompt = nullptr;
    Label *_timer_lbl = nullptr;
    Label *_gear_lbl = nullptr;
    Label *_speed_lbl = nullptr;
    Label *_toast = nullptr;
    Label *_end_title = nullptr;
    Label *_end_stats = nullptr;
    Button *_report_btn = nullptr;

    bool _started = false;
    bool _success = false;
    float _timer = 0.0f;
    float _report_cooldown = 0.0f;
    float _toast_ttl = 0.0f;
    String _demo_log_path;
    bool _quit_pending = false;
    float _quit_timer = 0.0f;

    // ---- demo autopilot ----
    bool _demo = false;
    float _demo_t = 0.0f;
    float _dump_timer = 0.0f;
    float _settle = 0.0f;
    int _demo_phase = 0;
    float _fwd_start_x = 0.0f;

    void build_environment();
    void build_camera();
    void build_hud();
    void load_level();
    void read_controls();
    void show_toast(const String &text);
    void save_screenshot(const String &name);

    // slot check: 4 corners inside + angle + stillness
    bool slot_fit(int &in_count, bool &angle_ok) const;

    // demo helpers
    void demo_tick(float dt);
    static float wrap_angle(float a);
    static void path_carrot(const Vector2 &p, float ahead, Vector2 &carrot,
                            float &end_dist);
    void demo_log_line(const String &line);

    Label *make_label(const String &text, int size, const Color &color,
                      HorizontalAlignment align);
};

} // namespace godot
