#include "level.h"
#include "car.h"

#include <godot_cpp/classes/box_mesh.hpp>
#include <godot_cpp/classes/box_shape3d.hpp>
#include <godot_cpp/classes/collision_shape3d.hpp>
#include <godot_cpp/classes/mesh_instance3d.hpp>
#include <godot_cpp/classes/static_body3d.hpp>
#include <godot_cpp/classes/standard_material3d.hpp>
#include <godot_cpp/core/math.hpp>

namespace godot {

void CppLevel::add_static_box(const Vector3 &center, const Vector3 &size,
                              const Color &color, const String &name) {
    StaticBody3D *body = memnew(StaticBody3D);
    body->set_name(name);
    body->set_position(center);

    CollisionShape3D *shape = memnew(CollisionShape3D);
    Ref<BoxShape3D> box;
    box.instantiate();
    box->set_size(size);
    shape->set_shape(box);
    body->add_child(shape);

    MeshInstance3D *mesh = memnew(MeshInstance3D);
    Ref<BoxMesh> bm;
    bm.instantiate();
    bm->set_size(size);
    mesh->set_mesh(bm);
    Ref<StandardMaterial3D> mat;
    mat.instantiate();
    mat->set_albedo(color);
    mesh->set_material_override(mat);
    body->add_child(mesh);
    add_child(body);
}

void CppLevel::paint_slot() {
    Node3D *pivot = memnew(Node3D);
    pivot->set_position(Vector3(SLOT_X, 0, SLOT_Z));

    MeshInstance3D *fill = memnew(MeshInstance3D);
    Ref<BoxMesh> fb;
    fb.instantiate();
    fb->set_size(Vector3(SLOT_LEN, 0.02f, SLOT_WID));
    fill->set_mesh(fb);
    Ref<StandardMaterial3D> fill_mat;
    fill_mat.instantiate();
    fill_mat->set_albedo(Color(0.25f, 0.68f, 0.36f, 0.32f));
    fill_mat->set_transparency(BaseMaterial3D::TRANSPARENCY_ALPHA);
    fill_mat->set_shading_mode(BaseMaterial3D::SHADING_MODE_UNSHADED);
    fill->set_material_override(fill_mat);
    fill->set_position(Vector3(0, 0.015f, 0));
    pivot->add_child(fill);

    Ref<StandardMaterial3D> white;
    white.instantiate();
    white->set_albedo(Color(0.92f, 0.92f, 0.90f));
    white->set_shading_mode(BaseMaterial3D::SHADING_MODE_UNSHADED);
    white->set_feature(BaseMaterial3D::FEATURE_EMISSION, true);
    white->set_emission(Color(0.72f, 0.72f, 0.66f));
    white->set_emission_energy_multiplier(0.9f);

    auto strip = [&](const Vector3 &size, const Vector3 &pos) {
        MeshInstance3D *m = memnew(MeshInstance3D);
        Ref<BoxMesh> bm;
        bm.instantiate();
        bm->set_size(size);
        m->set_mesh(bm);
        m->set_material_override(white);
        m->set_position(pos);
        pivot->add_child(m);
    };
    const float th = 0.10f;
    strip(Vector3(SLOT_LEN + th, 0.03f, th), Vector3(0, 0.02f, -SLOT_WID / 2.0f));
    strip(Vector3(SLOT_LEN + th, 0.03f, th), Vector3(0, 0.02f, SLOT_WID / 2.0f));
    strip(Vector3(th, 0.03f, SLOT_WID), Vector3(-SLOT_LEN / 2.0f, 0.02f, 0));
    strip(Vector3(th, 0.03f, SLOT_WID), Vector3(SLOT_LEN / 2.0f, 0.02f, 0));
    add_child(pivot);
}

void CppLevel::paint_road() {
    const float lane_z = SLOT_Z + SLOT_WID / 2.0f + 2.2f;

    Ref<StandardMaterial3D> yellow;
    yellow.instantiate();
    yellow->set_albedo(Color(0.9f, 0.72f, 0.08f));
    yellow->set_shading_mode(BaseMaterial3D::SHADING_MODE_UNSHADED);
    yellow->set_feature(BaseMaterial3D::FEATURE_EMISSION, true);
    yellow->set_emission(Color(0.5f, 0.4f, 0.05f));
    yellow->set_emission_energy_multiplier(0.55f);

    Ref<StandardMaterial3D> white;
    white.instantiate();
    white->set_albedo(Color(0.92f, 0.92f, 0.90f));
    white->set_shading_mode(BaseMaterial3D::SHADING_MODE_UNSHADED);
    white->set_feature(BaseMaterial3D::FEATURE_EMISSION, true);
    white->set_emission(Color(0.72f, 0.72f, 0.66f));
    white->set_emission_energy_multiplier(0.6f);

    auto flat = [&](const Vector3 &size, const Vector3 &pos,
                    const Ref<StandardMaterial3D> &mat, float yaw = 0.0f) {
        MeshInstance3D *m = memnew(MeshInstance3D);
        Ref<BoxMesh> bm;
        bm.instantiate();
        bm->set_size(size);
        m->set_mesh(bm);
        m->set_material_override(mat);
        m->set_position(pos);
        m->set_rotation(Vector3(0, yaw, 0));
        add_child(m);
    };

    // dashed yellow centerline
    for (float x = SLOT_X - 7.0f; x <= SLOT_X + 7.0f; x += 3.4f) {
        flat(Vector3(1.8f, 0.02f, 0.12f), Vector3(x, 0.044f, lane_z), yellow);
    }

    // arrow (shaft + chevron head), pointing west along the approach lane
    float ax = SLOT_X + 6.0f;
    float tip_x = ax - 0.85f;
    flat(Vector3(1.3f, 0.02f, 0.18f), Vector3(ax, 0.044f, lane_z), white);
    for (float s : { -1.0f, 1.0f }) {
        flat(Vector3(0.16f, 0.02f, 0.55f),
             Vector3(tip_x + 0.21f, 0.044f, lane_z + s * 0.2f), white, -s * 0.7f);
    }

    // zebra crossing
    float zc = SLOT_Z + SLOT_WID / 2.0f + 5.3f;
    for (float x = SLOT_X - 3.5f; x <= SLOT_X + 3.5f; x += 0.75f) {
        flat(Vector3(0.45f, 0.02f, 2.2f), Vector3(x, 0.044f, zc), white);
    }
}

void CppLevel::add_parked_car(const Vector3 &pos, float yaw_deg, const Color &color) {
    Node3D *body = memnew(Node3D);
    body->set_position(pos);
    body->set_rotation(Vector3(0, Math::deg_to_rad(yaw_deg), 0));

    Ref<StandardMaterial3D> paint;
    paint.instantiate();
    paint->set_albedo(color);
    paint->set_roughness(0.4f);
    paint->set_metallic(0.1f);

    Ref<StandardMaterial3D> glass;
    glass.instantiate();
    glass->set_albedo(Color(0.10f, 0.13f, 0.16f, 0.85f));
    glass->set_transparency(BaseMaterial3D::TRANSPARENCY_ALPHA);
    glass->set_roughness(0.15f);
    glass->set_metallic(0.45f);

    auto mesh = [&](const Vector3 &size, const Ref<StandardMaterial3D> &mat,
                    const Vector3 &mpos) {
        MeshInstance3D *m = memnew(MeshInstance3D);
        Ref<BoxMesh> bm;
        bm.instantiate();
        bm->set_size(size);
        m->set_mesh(bm);
        m->set_material_override(mat);
        m->set_position(mpos);
        body->add_child(m);
    };

    mesh(Vector3(CppCar::SEDAN_WID, 0.55f, CppCar::SEDAN_LEN), paint, Vector3(0, 0.52f, 0));
    mesh(Vector3(CppCar::SEDAN_WID - 0.14f, 0.30f, 2.16f), glass, Vector3(0, 0.95f, 0.25f));
    mesh(Vector3(CppCar::SEDAN_WID - 0.2f, 0.20f, 2.0f), paint, Vector3(0, 1.20f, 0.22f));
    add_child(body);
}

void CppLevel::build_level1() {
    add_static_box(Vector3(0, -0.5f, 0), Vector3(90, 1, 90), Color(0.24f, 0.25f, 0.27f),
                   String("Floor"));
    paint_slot();
    paint_road();

    add_static_box(Vector3(7, 0.175f, -4.6f), Vector3(24, 0.35f, 0.35f),
                   Color(0.72f, 0.72f, 0.70f), String("Obstacle"));
    add_static_box(Vector3(7, 0.5f, 6.6f), Vector3(24, 1.0f, 0.4f),
                   Color(0.72f, 0.72f, 0.70f), String("Obstacle"));
    add_static_box(Vector3(-4.5f, 0.5f, 1.0f), Vector3(0.4f, 1.0f, 11),
                   Color(0.72f, 0.72f, 0.70f), String("Obstacle"));
    add_static_box(Vector3(18.5f, 0.5f, 1.0f), Vector3(0.4f, 1.0f, 11),
                   Color(0.72f, 0.72f, 0.70f), String("Obstacle"));

    add_parked_car(Vector3(0.9f, 0, -2.6f), 0.0f, Color(0.75f, 0.77f, 0.80f));
    add_parked_car(Vector3(12.1f, 0, -2.6f), 0.0f, Color(0.25f, 0.40f, 0.75f));
}

} // namespace godot
