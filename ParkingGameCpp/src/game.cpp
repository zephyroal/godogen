#include "game.h"

#include <godot_cpp/classes/directional_light3d.hpp>
#include <godot_cpp/classes/environment.hpp>
#include <godot_cpp/classes/file_access.hpp>
#include <godot_cpp/classes/image.hpp>
#include <godot_cpp/classes/input.hpp>
#include <godot_cpp/classes/input_event_key.hpp>
#include <godot_cpp/classes/light3d.hpp>
#include <godot_cpp/classes/os.hpp>
#include <godot_cpp/classes/procedural_sky_material.hpp>
#include <godot_cpp/classes/project_settings.hpp>
#include <godot_cpp/classes/scene_tree.hpp>
#include <godot_cpp/classes/sky.hpp>
#include <godot_cpp/classes/system_font.hpp>
#include <godot_cpp/classes/viewport.hpp>
#include <godot_cpp/classes/viewport_texture.hpp>
#include <godot_cpp/classes/world_environment.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/core/math.hpp>
#include <godot_cpp/core/math_defs.hpp>

#include <cmath>
#include <cstdio>
#include <filesystem>
#include <fstream>

namespace godot {

// rear-axle path of the demo autopilot — kinematically exact two-arc S
// (r = 4.8 m), identical to the C# reference implementation
static const Vector2 DEMO_PATH[8] = {
    Vector2(12.32f, 2.20f), Vector2(10.91f, 1.99f), Vector2(9.65f, 1.39f),
    Vector2(8.59f, 0.39f),  Vector2(8.08f, -0.35f), Vector2(7.23f, -1.49f),
    Vector2(6.24f, -2.26f), Vector2(4.84f, -2.79f),
};
static const float DOCK_X = 6.5f, DOCK_Z = -2.6f, DOCK_YAW_DEG = -90.0f;

void CppGame::_bind_methods() {
    ClassDB::bind_method(D_METHOD("report_parked"), &CppGame::report_parked);
}

Label *CppGame::make_label(const String &text, int size, const Color &color,
                           HorizontalAlignment align) {
    Label *l = memnew(Label);
    l->set_text(text);
    l->set_modulate(color);
    Ref<SystemFont> font;
    font.instantiate();
    PackedStringArray names;
    names.append(String("Microsoft YaHei"));
    names.append(String("SimHei"));
    names.append(String("Segoe UI"));
    names.append(String("sans-serif"));
    font->set_font_names(names);
    l->add_theme_font_override(String("font"), font);
    l->add_theme_font_size_override(String("font_size"), size);
    l->set_horizontal_alignment(align);
    return l;
}

void CppGame::build_environment() {
    Ref<ProceduralSkyMaterial> sky_mat;
    sky_mat.instantiate();
    sky_mat->set_sky_top_color(Color(0.32f, 0.48f, 0.72f));
    sky_mat->set_sky_horizon_color(Color(0.78f, 0.82f, 0.88f));
    sky_mat->set_ground_bottom_color(Color(0.28f, 0.27f, 0.24f));

    Ref<Environment> env;
    env.instantiate();
    env->set_background(Environment::BG_SKY);
    Ref<Sky> sky;
    sky.instantiate();
    env->set_sky(sky);
    sky->set_material(sky_mat);
    env->set_ambient_source(Environment::AMBIENT_SOURCE_SKY);
    env->set_ambient_light_energy(0.65f);
    env->set_tonemapper(Environment::TONE_MAPPER_FILMIC);
    env->set_ssao_enabled(true);
    env->set_glow_enabled(true);
    env->set_glow_intensity(0.65f);
    env->set_glow_hdr_bleed_threshold(0.95f);

    WorldEnvironment *we = memnew(WorldEnvironment);
    we->set_environment(env);
    add_child(we);

    DirectionalLight3D *sun = memnew(DirectionalLight3D);
    sun->set_shadow(true);
    sun->set_param(Light3D::PARAM_ENERGY, 1.35f);
    sun->set_color(Color(1.0f, 0.95f, 0.86f)); // warm afternoon sun
    sun->set_param(Light3D::PARAM_SHADOW_MAX_DISTANCE, 70.0f);
    sun->set_param(Light3D::PARAM_SHADOW_BLUR, 1.5f);
    sun->set_rotation_degrees(Vector3(-62.0f, 35.0f, 0.0f));
    add_child(sun);

    get_viewport()->set_msaa_3d(Viewport::MSAA_8X);
    get_viewport()->set_screen_space_aa(Viewport::SCREEN_SPACE_AA_FXAA);
}

void CppGame::build_camera() {
    _cam = memnew(Camera3D);
    _cam->set_fov(42.0f);
    _cam->set_near(0.1f);
    _cam->set_far(300.0f);
    add_child(_cam);
    _cam->make_current();
}

void CppGame::build_hud() {
    _hud = memnew(CanvasLayer);
    add_child(_hud);

    _prompt = make_label(String("第 1 关 · 侧方入库 —— 开过车位后挂 R 倒回"), 24,
                         Color(1.0f, 0.96f, 0.88f), HORIZONTAL_ALIGNMENT_CENTER);
    _prompt->set_anchors_and_offsets_preset(Control::PRESET_CENTER_TOP);
    _prompt->set_offset(SIDE_LEFT, -420);
    _prompt->set_offset(SIDE_RIGHT, 420);
    _prompt->set_offset(SIDE_TOP, 12);
    _prompt->set_offset(SIDE_BOTTOM, 52);
    _hud->add_child(_prompt);

    _timer_lbl = make_label(String("0.0s"), 20, Color(0.9f, 0.92f, 0.95f),
                            HORIZONTAL_ALIGNMENT_RIGHT);
    _timer_lbl->set_anchors_and_offsets_preset(Control::PRESET_TOP_RIGHT);
    _timer_lbl->set_offset(SIDE_LEFT, -200);
    _timer_lbl->set_offset(SIDE_RIGHT, -18);
    _timer_lbl->set_offset(SIDE_TOP, 14);
    _timer_lbl->set_offset(SIDE_BOTTOM, 44);
    _hud->add_child(_timer_lbl);

    _gear_lbl = make_label(String("N"), 64, Color(0.92f, 0.90f, 0.86f),
                           HORIZONTAL_ALIGNMENT_CENTER);
    _gear_lbl->set_anchors_and_offsets_preset(Control::PRESET_BOTTOM_LEFT);
    _gear_lbl->set_offset(SIDE_LEFT, 24);
    _gear_lbl->set_offset(SIDE_RIGHT, 104);
    _gear_lbl->set_offset(SIDE_TOP, -96);
    _gear_lbl->set_offset(SIDE_BOTTOM, -24);
    _hud->add_child(_gear_lbl);

    _speed_lbl = make_label(String("0 km/h"), 34, Color(0.92f, 0.90f, 0.86f),
                            HORIZONTAL_ALIGNMENT_RIGHT);
    _speed_lbl->set_anchors_and_offsets_preset(Control::PRESET_BOTTOM_RIGHT);
    _speed_lbl->set_offset(SIDE_LEFT, -220);
    _speed_lbl->set_offset(SIDE_RIGHT, -24);
    _speed_lbl->set_offset(SIDE_TOP, -84);
    _speed_lbl->set_offset(SIDE_BOTTOM, -36);
    _hud->add_child(_speed_lbl);

    _toast = make_label(String(""), 22, Color(1.0f, 0.62f, 0.55f),
                        HORIZONTAL_ALIGNMENT_CENTER);
    _toast->set_anchors_and_offsets_preset(Control::PRESET_CENTER_TOP);
    _toast->set_offset(SIDE_LEFT, -430);
    _toast->set_offset(SIDE_RIGHT, 430);
    _toast->set_offset(SIDE_TOP, 64);
    _toast->set_offset(SIDE_BOTTOM, 116);
    _toast->set_visible(false);
    _hud->add_child(_toast);

    _report_btn = memnew(Button);
    _report_btn->set_text(String("报告我停好了 (G)"));
    _report_btn->set_focus_mode(Control::FOCUS_NONE);
    Ref<SystemFont> bfont;
    bfont.instantiate();
    PackedStringArray bnames;
    bnames.append(String("Microsoft YaHei"));
    bnames.append(String("SimHei"));
    bnames.append(String("Segoe UI"));
    bnames.append(String("sans-serif"));
    bfont->set_font_names(bnames);
    _report_btn->add_theme_font_override(String("font"), bfont);
    _report_btn->add_theme_font_size_override(String("font_size"), 24);
    _report_btn->set_anchors_and_offsets_preset(Control::PRESET_CENTER_BOTTOM);
    _report_btn->set_offset(SIDE_LEFT, -140);
    _report_btn->set_offset(SIDE_RIGHT, 140);
    _report_btn->set_offset(SIDE_TOP, -70);
    _report_btn->set_offset(SIDE_BOTTOM, -18);
    _report_btn->connect(String("pressed"), Callable(this, String("report_parked")));
    _hud->add_child(_report_btn);

    // start overlay
    _start_overlay = memnew(Control);
    _start_overlay->set_mouse_filter(Control::MOUSE_FILTER_STOP);
    _start_overlay->set_anchors_and_offsets_preset(Control::PRESET_FULL_RECT);
    ColorRect *dim = memnew(ColorRect);
    dim->set_color(Color(0, 0, 0, 0.72f));
    dim->set_anchors_and_offsets_preset(Control::PRESET_FULL_RECT);
    _start_overlay->add_child(dim);
    Label *title = make_label(String("3D 倒车入库 (C++ GDExtension 版)"), 56,
                              Color(1.0f, 0.88f, 0.6f), HORIZONTAL_ALIGNMENT_CENTER);
    title->set_position(Vector2(200, 140));
    title->set_size(Vector2(880, 90));
    _start_overlay->add_child(title);
    Label *keys = make_label(
        String("↑ 油门 · ↓ 刹车 · ←→ 方向 · R/N/D 挂挡 · 空格 手刹 · G 报告我停好了 · 回车开始"),
        20, Color(0.78f, 0.72f, 0.62f), HORIZONTAL_ALIGNMENT_CENTER);
    keys->set_position(Vector2(140, 560));
    keys->set_size(Vector2(1000, 40));
    _start_overlay->add_child(keys);
    _hud->add_child(_start_overlay);

    // verdict overlay (hidden)
    _end_overlay = memnew(Control);
    _end_overlay->set_visible(false);
    ColorRect *dim2 = memnew(ColorRect);
    dim2->set_color(Color(0, 0, 0, 0.62f));
    dim2->set_anchors_and_offsets_preset(Control::PRESET_FULL_RECT);
    _end_overlay->add_child(dim2);
    _end_title = make_label(String(""), 62, Color(0.55f, 1.0f, 0.65f),
                            HORIZONTAL_ALIGNMENT_CENTER);
    _end_title->set_position(Vector2(100, 280));
    _end_title->set_size(Vector2(1080, 90));
    _end_overlay->add_child(_end_title);
    _end_stats = make_label(String(""), 28, Color(0.95f, 0.92f, 0.86f),
                            HORIZONTAL_ALIGNMENT_CENTER);
    _end_stats->set_position(Vector2(100, 380));
    _end_stats->set_size(Vector2(1080, 50));
    _end_overlay->add_child(_end_stats);
    Label *hint = make_label(String("回车 · 再来一次"), 24, Color(1.0f, 0.88f, 0.6f),
                             HORIZONTAL_ALIGNMENT_CENTER);
    hint->set_position(Vector2(100, 460));
    hint->set_size(Vector2(1080, 40));
    _end_overlay->add_child(hint);
    _hud->add_child(_end_overlay);
}

void CppGame::load_level() {
    if (_level) {
        remove_child(_level);
        _level->queue_free();
    }
    _level = memnew(CppLevel);
    _level->build_level1();
    add_child(_level);

    _car->set_global_position(Vector3(13.8f, 0.8f, 2.2f));
    _car->set_rotation(Vector3(0, Math::deg_to_rad(-90.0f), 0));
    _car->set_linear_velocity(Vector3());
    _car->set_angular_velocity(Vector3());
    _car->select_gear(CppCar::GEAR_N);
    _car->throttle = 0;
    _car->brake_input = 1;
    _car->set_brake(7.0f);
    _car->set_sleeping(false); // mirror of the C# ResetTo wake-up
    _timer = 0.0f;
    _success = false;
    _report_cooldown = 0.0f;

    Vector3 p = _car->get_global_position();
    _cam->set_global_position(p + Vector3(0, 15, 7));
    _cam->look_at(p, Vector3(0, 1, 0));
}

void CppGame::_ready() {
    build_environment();
    build_camera();

    _car = memnew(CppCar);
    _car->set_name(String("PlayerCar"));
    add_child(_car);

    build_hud();

    PackedStringArray args = OS::get_singleton()->get_cmdline_user_args();
    for (int i = 0; i < args.size(); i++) {
        if (args[i] == String("--demo")) {
            _demo = true;
        }
    }

    if (_demo) {
        _demo_log_path = ProjectSettings::get_singleton()->globalize_path(
            String("res://demo_state.txt"));
        std::ofstream out(_demo_log_path.utf8().get_data(), std::ios::trunc);
        out << "cpp demo start level=1" << std::endl;
    }

    load_level();
    _started = _demo;
    _start_overlay->set_visible(!_demo);
}

void CppGame::read_controls() {
    Input *in = Input::get_singleton();
    _car->throttle = in->is_key_pressed(KEY_UP) ? 1.0f : 0.0f;
    _car->brake_input = in->is_key_pressed(KEY_DOWN) ? 1.0f : 0.0f;
    float steer = 0.0f;
    if (in->is_key_pressed(KEY_LEFT)) steer += 1.0f;
    if (in->is_key_pressed(KEY_RIGHT)) steer -= 1.0f;
    _car->steer_input = steer;
    _car->handbrake = in->is_key_pressed(KEY_SPACE);
}

void CppGame::show_toast(const String &text) {
    _toast->set_text(String("× 未通过：") + text);
    _toast->set_visible(true);
    _toast_ttl = 4.0f;
}

void CppGame::save_screenshot(const String &name) {
    String dir = ProjectSettings::get_singleton()->globalize_path(String("res://screenshots"));
    std::error_code ec;
    std::filesystem::create_directories(dir.utf8().get_data(), ec);
    String path = ProjectSettings::get_singleton()->globalize_path(
        String("res://screenshots/") + name);
    Ref<Image> img = get_viewport()->get_texture()->get_image();
    if (img.is_valid()) {
        img->save_png(path);
    }
}

bool CppGame::slot_fit(int &in_count, bool &angle_ok) const {
    in_count = 0;
    const float hx = CppLevel::SLOT_LEN / 2.0f - 0.04f;
    const float hz = CppLevel::SLOT_WID / 2.0f - 0.04f;
    for (const Vector3 &c : _car->corners()) {
        float lx = c.x - CppLevel::SLOT_X;
        float lz = c.z - CppLevel::SLOT_Z;
        if (Math::abs(lx) <= hx && Math::abs(lz) <= hz) {
            in_count++;
        }
    }
    Vector3 fwd = -_car->get_global_transform().basis.get_column(2);
    angle_ok = Math::abs(fwd.x) >= Math::cos(Math::deg_to_rad(CppLevel::ANGLE_TOL_DEG));
    return in_count == 4 && angle_ok;
}

void CppGame::report_parked() {
    if (!_started || _success || _report_cooldown > 0.0f) {
        return;
    }
    int in_count = 0;
    bool angle_ok = false;
    bool geom = slot_fit(in_count, angle_ok);
    bool still = _car->get_linear_velocity().length() < 0.12f;

    if (geom && still) {
        _success = true;
        bool perfect = Math::abs(_car->get_global_position().z - CppLevel::SLOT_Z) <= 0.15f;
        _end_title->set_text(perfect ? String("★ 完美入库！") : String("√ 停车入位成功！"));
        char buf[256];
        std::snprintf(buf, sizeof(buf), "用时 %.1f 秒 · 角度误差 %.1f° · 横向居中偏差 %.0f cm",
            _timer, Math::rad_to_deg(Math::acos(Math::clamp(
                Math::abs(-_car->get_global_transform().basis.get_column(2).x), 0.0f, 1.0f))),
            Math::abs(_car->get_global_position().z - CppLevel::SLOT_Z) * 100.0f);
        _end_stats->set_text(String::utf8(buf));
        _end_overlay->set_visible(true);
        if (_demo) {
            demo_log_line(String("RESULT SUCCESS time=") + String::num(_timer, 2));
            save_screenshot(String("demo_success.png"));
            // small delay so the verdict overlay lands in the shot
            _toast_ttl = 0.0f; // unused, quit handled by _physics_process below
            _quit_pending = true;
            _quit_timer = 1.5f;
        }
    } else {
        _report_cooldown = 1.5f;
        if (!still) {
            show_toast(String("车辆还在移动，停稳后再报告"));
        } else if (in_count < 4) {
            char buf[64];
            std::snprintf(buf, sizeof(buf), "车身只有 %d/4 个角在库位内", in_count);
            show_toast(String::utf8(buf));
        } else {
            show_toast(String("车身与库位长轴夹角超过 15° 容差"));
        }
    }
}

void CppGame::_input(const Ref<InputEvent> &event) {
    InputEventKey *k = Object::cast_to<InputEventKey>(event.ptr());
    if (k == nullptr || !k->is_pressed() || k->is_echo()) {
        return;
    }
    if (!_started) {
        if (k->get_keycode() == KEY_ENTER || k->get_keycode() == KEY_KP_ENTER) {
            _start_overlay->set_visible(false);
            _started = true;
        }
        return;
    }
    if (_success) {
        if (k->get_keycode() == KEY_ENTER) {
            load_level();
            _end_overlay->set_visible(false);
        }
        return;
    }
    switch (k->get_keycode()) {
        case KEY_R:
            _car->select_gear(CppCar::GEAR_R);
            _gear_lbl->set_text(String("R"));
            _gear_lbl->set_modulate(Color(1.0f, 0.35f, 0.28f));
            break;
        case KEY_N:
            _car->select_gear(CppCar::GEAR_N);
            _gear_lbl->set_text(String("N"));
            _gear_lbl->set_modulate(Color(0.92f, 0.90f, 0.86f));
            break;
        case KEY_D:
            _car->select_gear(CppCar::GEAR_D);
            _gear_lbl->set_text(String("D"));
            _gear_lbl->set_modulate(Color(0.45f, 0.95f, 0.55f));
            break;
        case KEY_G:
            report_parked();
            break;
        case KEY_ENTER:
            load_level();
            break;
        default:
            break;
    }
}

float CppGame::wrap_angle(float a) {
    while (a > Math::PI) a -= Math::TAU;
    while (a < -Math::PI) a += Math::TAU;
    return a;
}

void CppGame::path_carrot(const Vector2 &p, float ahead, Vector2 &carrot,
                          float &end_dist) {
    int best = 0;
    float best_t = 0.0f, best_d = 1e18f;
    for (int i = 0; i < 7; i++) {
        Vector2 a = DEMO_PATH[i];
        Vector2 ab = DEMO_PATH[i + 1] - a;
        float t = Math::clamp((p - a).dot(ab) / ab.length_squared(), 0.0f, 1.0f);
        float d = (p - (a + ab * t)).length_squared();
        if (d < best_d) {
            best_d = d;
            best = i;
            best_t = t;
        }
    }
    Vector2 seg = DEMO_PATH[best + 1] - DEMO_PATH[best];
    end_dist = (1.0f - best_t) * seg.length();
    for (int i = best + 1; i < 7; i++) {
        end_dist += (DEMO_PATH[i + 1] - DEMO_PATH[i]).length();
    }
    Vector2 cur = DEMO_PATH[best] + seg * best_t;
    float remain = ahead;
    int j = best;
    while (remain > 1e-4f && j < 7) {
        Vector2 nxt = DEMO_PATH[j + 1];
        float d = (nxt - cur).length();
        if (d <= remain) {
            cur = nxt;
            j++;
            remain -= d;
        } else {
            cur = cur + (nxt - cur) / d * remain;
            remain = 0.0f;
        }
    }
    carrot = cur;
}

void CppGame::demo_log_line(const String &line) {
    std::ofstream out(_demo_log_path.utf8().get_data(), std::ios::app);
    out << line.utf8().get_data() << std::endl;
}

void CppGame::demo_tick(float dt) {
    _demo_t += dt;

    if (_demo_t > 30.0f && !_success) {
        demo_log_line(String("RESULT FAILED timeout"));
        save_screenshot(String("demo_failed.png"));
        get_tree()->quit(1);
        return;
    }

    if (_demo_phase == 4) {
        // done — settle, then submit the same report a human player would press
        _car->throttle = 0.0f;
        _car->brake_input = 1.0f;
        _car->steer_input = 0.0f;
        _car->handbrake = true;
        _car->select_gear(CppCar::GEAR_N);
        _gear_lbl->set_text(String("N"));

        if (_car->get_linear_velocity().length() < 0.12f) {
            _settle += dt;
            if (_settle > 0.8f && _report_cooldown <= 0.0f) {
                _settle = 0.0f;
                report_parked();
            }
        } else {
            _settle = 0.0f;
        }
        return;
    }

    const Basis b = _car->get_global_transform().basis;
    Vector3 rear3 = _car->get_global_position() + b.get_column(2) * 1.48f;
    Vector2 rear(rear3.x, rear3.z);
    Vector2 rear_dir = Vector2(b.get_column(2).x, b.get_column(2).z).normalized();

    Vector3 pos = _car->get_global_position();
    float lz = pos.z - DOCK_Z;
    float h = wrap_angle(_car->get_rotation().y - Math::deg_to_rad(DOCK_YAW_DEG));

    float steer = 0.0f, vt = 0.0f;
    if (_demo_phase == 0) {
        float look = Math::clamp(0.75f + 0.5f * Math::abs(_car->get_forward_speed()), 0.9f, 1.35f);
        Vector2 carrot;
        float end_dist = 0.0f;
        path_carrot(rear, look, carrot, end_dist);
        Vector2 to = carrot - rear;
        float desired = Math::atan2(to.y, to.x);
        float heading = Math::atan2(rear_dir.y, rear_dir.x);
        float err = wrap_angle(desired - heading);
        float d_rad = Math::atan(2.0f * 2.96f * Math::sin(err) / Math::max(look, 0.2f));
        steer = Math::clamp(d_rad / Math::deg_to_rad(32.0f), -1.0f, 1.0f);
        vt = end_dist > 2.2f ? 0.9f : Math::clamp(0.55f * end_dist, 0.45f, 0.9f);
        if (end_dist < 0.15f) {
            _demo_phase = 1;
        }
    } else {
        steer = Math::clamp(2.5f * h + 1.1f * lz, -1.0f, 1.0f);
        vt = Math::clamp(0.45f + 0.55f * Math::abs(h), 0.45f, 1.1f);

        int in_count = 0;
        bool angle_ok = false;
        bool centered = slot_fit(in_count, angle_ok);
        bool docked = pos.x < DOCK_X + 0.25f;

        if (_demo_phase == 1) {
            if (centered) {
                _demo_phase = 4;
            } else if (docked) {
                _demo_phase = 2;
                _fwd_start_x = pos.x;
            }
        } else if (_demo_phase == 2) {
            // forward straighten: in D, steering LEFT raises yaw (sign flips vs R)
            _car->select_gear(CppCar::GEAR_D);
            steer = Math::clamp(-4.5f * h + 0.8f * lz, -1.0f, 1.0f);
            vt = 0.55f;
            if (Math::abs(h) < 0.05f || pos.x - _fwd_start_x > 1.0f || pos.x > DOCK_X + 0.95f) {
                _demo_phase = 3;
            }
        } else if (_demo_phase == 3) {
            if (centered || pos.x < DOCK_X - 0.75f) {
                _demo_phase = 4;
            }
        }
    }

    float v = -_car->get_forward_speed();
    if (_demo_phase == 2) {
        v = _car->get_forward_speed();
    }
    float throttle = Math::clamp((vt - Math::abs(v)) * 1.2f, 0.0f, 0.85f);
    float brake = (Math::abs(v) > vt + 0.3f || vt <= 0.01f)
        ? Math::clamp((Math::abs(v) - vt) * 0.8f, 0.0f, 1.0f) : 0.0f;

    if (_demo_phase != 2) {
        _car->select_gear(CppCar::GEAR_R);
        _gear_lbl->set_text(String("R"));
    } else {
        _gear_lbl->set_text(String("D"));
    }
    _car->steer_input = steer;
    _car->throttle = throttle;
    _car->brake_input = brake;
    _car->handbrake = false;

    _dump_timer -= dt;
    if (_dump_timer <= 0.0f) {
        _dump_timer = 0.2f;
        char buf[320];
        int in_count = 0;
        bool angle_ok = false;
        slot_fit(in_count, angle_ok);
        std::snprintf(buf, sizeof(buf),
            "t=%.2f gear=%c spd=%.2f pos=(%.2f,%.2f) yaw=%.1f steer=%.1f ph=%d "
            "lz=%.3f h=%.3f inSlot=%d angleOK=%d",
            _demo_t, (_car->get_gear() == CppCar::GEAR_D ? 'D' : 'R'),
            _car->get_forward_speed(), pos.x, pos.z,
            Math::rad_to_deg(_car->get_rotation().y),
            Math::rad_to_deg(_car->get_steering()), _demo_phase, lz, h,
            in_count, angle_ok ? 1 : 0);
        demo_log_line(String::utf8(buf));
    }
}

void CppGame::_physics_process(double delta) {
    float dt = static_cast<float>(delta);

    if (_success) {
        if (_quit_pending) {
            _quit_timer -= dt;
            if (_quit_timer <= 0.0f) {
                get_tree()->quit(0);
            }
        }
        return;
    }
    if (!_started) {
        return;
    }

    _report_cooldown = Math::max(0.0f, _report_cooldown - dt);
    _car->set_brake_lights(_car->brake_input > 0.5f || _car->handbrake);

    if (_demo) {
        demo_tick(dt);
    } else {
        read_controls();
    }
    if (_success) {
        return; // demo_tick just reported successfully
    }

    _timer += dt;
    char buf[64];
    std::snprintf(buf, sizeof(buf), "%.1fs", _timer);
    _timer_lbl->set_text(String::utf8(buf));
}

void CppGame::_process(double delta) {
    if (_toast_ttl > 0.0f) {
        _toast_ttl -= static_cast<float>(delta);
        if (_toast_ttl <= 0.0f) {
            _toast->set_visible(false);
        }
    }
    if (!_started || _car == nullptr) {
        return;
    }
    // top-down follow camera (fixed north-up)
    Vector3 p = _car->get_global_position();
    Vector3 desired = p + Vector3(0, 15, 7);
    float k = Math::clamp(static_cast<float>(delta) * 5.0f, 0.0f, 1.0f);
    _cam->set_global_position(_cam->get_global_position().lerp(desired, k));
    _cam->look_at(p + Vector3(0, 0.2f, 0), Vector3(0, 1, 0));

    float kmh = Math::abs(_car->get_forward_speed()) * 3.6f;
    char buf[64];
    std::snprintf(buf, sizeof(buf), "%.0f km/h", kmh);
    _speed_lbl->set_text(String::utf8(buf));
}

} // namespace godot
