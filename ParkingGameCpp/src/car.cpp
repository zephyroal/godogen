#include "car.h"

#include <godot_cpp/classes/box_mesh.hpp>
#include <godot_cpp/classes/box_shape3d.hpp>
#include <godot_cpp/classes/collision_shape3d.hpp>
#include <godot_cpp/classes/cylinder_mesh.hpp>
#include <godot_cpp/classes/mesh_instance3d.hpp>
#include <godot_cpp/core/math.hpp>

namespace godot {

float CppCar::get_forward_speed() const {
    return -get_global_transform().basis.get_column(2).dot(get_linear_velocity());
}

std::vector<Vector3> CppCar::corners() const {
    const Basis &b = get_global_transform().basis;
    const Vector3 o = get_global_position();
    Vector3 hw = b.get_column(0) * (SEDAN_WID * 0.5f);
    Vector3 hf = b.get_column(2) * (SEDAN_LEN * 0.5f);
    return { o + hw + hf, o + hw - hf, o - hw - hf, o - hw + hf };
}

void CppCar::set_brake_lights(bool on) {
    if (_tail.is_valid()) {
        _tail->set_emission_energy_multiplier(on ? 4.5f : 1.6f);
    }
}

VehicleWheel3D *CppCar::make_wheel(const Vector3 &pos, bool steering, bool traction) {
    VehicleWheel3D *w = memnew(VehicleWheel3D);
    w->set_position(pos);
    w->set_radius(0.34f);
    w->set_suspension_rest_length(0.35f);
    w->set_suspension_travel(0.20f);
    w->set_suspension_stiffness(55.0f);
    w->set_damping_compression(6.0f);
    w->set_damping_relaxation(7.5f);
    w->set_suspension_max_force(60000.0f);
    w->set_friction_slip(11.0f);
    w->set_roll_influence(0.08f);
    w->set_use_as_steering(steering);
    w->set_use_as_traction(traction);
    add_child(w);
    return w;
}

void CppCar::_ready() {
    set_mass(1200.0f);
    set_can_sleep(false);

    CollisionShape3D *shape = memnew(CollisionShape3D);
    Ref<BoxShape3D> box;
    box.instantiate();
    box->set_size(Vector3(SEDAN_WID, 1.05f, SEDAN_LEN));
    shape->set_shape(box);
    shape->set_position(Vector3(0, 0.55f, 0));
    add_child(shape);

    _fl = make_wheel(Vector3(-0.80f, 0.10f, -1.48f), true, false);
    _fr = make_wheel(Vector3(0.80f, 0.10f, -1.48f), true, false);
    _rl = make_wheel(Vector3(-0.80f, 0.10f, 1.48f), false, true);
    _rr = make_wheel(Vector3(0.80f, 0.10f, 1.48f), false, true);

    build_visuals();
}

void CppCar::build_visuals() {
    Ref<StandardMaterial3D> red;
    red.instantiate();
    red->set_albedo(Color(0.82f, 0.27f, 0.24f));
    red->set_roughness(0.32f);
    red->set_metallic(0.12f);

    Ref<StandardMaterial3D> dark;
    dark.instantiate();
    dark->set_albedo(Color(0.14f, 0.15f, 0.17f));
    dark->set_roughness(0.5f);

    Ref<StandardMaterial3D> glass;
    glass.instantiate();
    glass->set_albedo(Color(0.10f, 0.13f, 0.16f, 0.85f));
    glass->set_transparency(BaseMaterial3D::TRANSPARENCY_ALPHA);
    glass->set_roughness(0.08f);
    glass->set_metallic(0.9f);

    Ref<StandardMaterial3D> tire;
    tire.instantiate();
    tire->set_albedo(Color(0.09f, 0.09f, 0.10f));
    tire->set_roughness(0.95f);

    Ref<StandardMaterial3D> light;
    light.instantiate();
    light->set_albedo(Color(1.0f, 0.97f, 0.85f));
    light->set_feature(BaseMaterial3D::FEATURE_EMISSION, true);
    light->set_emission(Color(1.0f, 0.95f, 0.8f));
    light->set_emission_energy_multiplier(1.6f);

    _tail.instantiate();
    _tail->set_albedo(Color(0.9f, 0.1f, 0.1f));
    _tail->set_feature(BaseMaterial3D::FEATURE_EMISSION, true);
    _tail->set_emission(Color(1.0f, 0.12f, 0.1f));
    _tail->set_emission_energy_multiplier(1.6f);

    auto mesh = [](const Vector3 &size, const Ref<StandardMaterial3D> &mat,
                   const Vector3 &pos) {
        MeshInstance3D *m = memnew(MeshInstance3D);
        Ref<BoxMesh> bm;
        bm.instantiate();
        bm->set_size(size);
        m->set_mesh(bm);
        m->set_material_override(mat);
        m->set_position(pos);
        return m;
    };

    add_child(mesh(Vector3(SEDAN_WID, 0.55f, SEDAN_LEN), red, Vector3(0, 0.52f, 0)));
    add_child(mesh(Vector3(SEDAN_WID - 0.14f, 0.30f, 2.16f), glass, Vector3(0, 0.95f, 0.25f)));
    add_child(mesh(Vector3(SEDAN_WID - 0.2f, 0.20f, 2.0f), red, Vector3(0, 1.20f, 0.22f)));

    for (float z : { -SEDAN_LEN / 2.0f - 0.03f, SEDAN_LEN / 2.0f + 0.03f }) {
        add_child(mesh(Vector3(SEDAN_WID + 0.06f, 0.30f, 0.12f), dark, Vector3(0, 0.38f, z)));
    }
    for (float x : { -0.55f, 0.55f }) {
        add_child(mesh(Vector3(0.30f, 0.12f, 0.06f), light, Vector3(x, 0.55f, -SEDAN_LEN / 2.0f - 0.02f)));
        add_child(mesh(Vector3(0.34f, 0.18f, 0.10f), _tail, Vector3(x, 0.56f, SEDAN_LEN / 2.0f + 0.04f)));
    }

    for (float x : { -0.80f, 0.80f }) {
        for (float z : { -1.48f, 1.48f }) {
            Node3D *pivot = memnew(Node3D);
            pivot->set_position(Vector3(x, 0.10f, z));
            MeshInstance3D *wheel = memnew(MeshInstance3D);
            Ref<CylinderMesh> cm;
            cm.instantiate();
            cm->set_top_radius(0.34f);
            cm->set_bottom_radius(0.34f);
            cm->set_height(0.26f);
            wheel->set_mesh(cm);
            wheel->set_material_override(tire);
            wheel->set_rotation(Vector3(0, 0, Math::PI / 2.0f)); // cylinder axis -> X
            pivot->add_child(wheel);
            add_child(pivot);
            if (z < 0) {
                if (x < 0) _fl_pivot = pivot;
                else _fr_pivot = pivot;
            }
        }
    }
}

void CppCar::_physics_process(double delta) {
    float dt = static_cast<float>(delta);
    float speed = get_forward_speed();

    // engine by gear (sign note: body-level force positive pushes +Z / tail)
    float engine = 0.0f;
    switch (current_gear) {
        case GEAR_D:
            if (throttle > 0.01f) {
                if (speed < 13.0f) {
                    float fade = Math::clamp(1.0f - speed / 13.0f, 0.0f, 1.0f);
                    engine = -3400.0f * throttle * (0.4f + 0.6f * fade);
                }
            } else if (speed > 0.3f) {
                engine = 550.0f; // coasting forward -> drag toward +Z
            }
            break;
        case GEAR_R:
            if (throttle > 0.01f) {
                if (speed > -6.5f) {
                    float fade = Math::clamp(1.0f + speed / 6.5f, 0.0f, 1.0f);
                    engine = 2400.0f * throttle * (0.4f + 0.6f * fade);
                }
            } else if (speed < -0.3f) {
                engine = -550.0f;
            }
            break;
        default:
            break;
    }
    set_engine_force(engine);

    float brake = brake_input * 7.0f;
    if (handbrake) {
        brake = Math::max(brake, 7.0f * 0.9f);
    }
    if (brake_input > 0.5f && Math::abs(speed) < 0.1f && throttle < 0.01f) {
        set_linear_damp(8.0f);
        brake = 7.0f;
    } else {
        set_linear_damp(0.0f);
    }
    set_brake(brake);

    // speed-sensitive steering + rate-limited actuator
    float max_steer = Math::deg_to_rad(34.0f) *
        Math::clamp(1.0f - Math::abs(speed) / 16.0f, 0.3f, 1.0f);
    float target = Math::clamp(steer_input, -1.0f, 1.0f) * max_steer;
    _steer = Math::lerp(_steer, target, Math::clamp(7.0f * dt, 0.0f, 1.0f));
    set_steering(_steer);
    if (_fl_pivot) _fl_pivot->set_rotation(Vector3(0, _steer, 0));
    if (_fr_pivot) _fr_pivot->set_rotation(Vector3(0, _steer, 0));
}

} // namespace godot
