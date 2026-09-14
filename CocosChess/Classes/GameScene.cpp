// GameScene.cpp — 3D Xiangqi on Cocos2d-x 4, synced with Godot 3DChess visual parameters
#include "GameScene.h"
#include "AudioEngine.h"
#include "2d/CCLight.h"
#include "platform/CCImage.h"
#include "renderer/CCTexture2D.h"
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <random>

USING_NS_CC;
using namespace chess;

// ═══════════════ Godot parameter constants ═══════════════

// Camera (Game.cs: Yaw/Pitch/Dist, target (0,0,0.3); clamps 0.45..1.35 / 7..20)
static constexpr float CAM_FOV    = 38.0f;
static constexpr float CAM_TARGET_Z = 0.3f;
static constexpr float CAM_EASE   = 6.0f;

// Board colors — RENDERED-parity values measured from the Godot reference's
// own output (result/frame00000200.png, 01_opening.png): its albedo × the
// reference light rig (sun 1.65 + ambient 0.5 + filmic tonemap) renders the
// slab near-cream (252,224,182), far brighter than the source albedo. The
// unlit board bakes that brightness, or it reads dark next to the lit models.
static const Color3B SLAB_COLOR   = Color3B(252, 224, 182); // reference-measured
static const Color3B STRIPE_COLOR = Color3B(232, 195, 146); // slab × (0.92,0.87,0.80)
static const Color3B DESK_COLOR   = Color3B(102, 73, 53);   // desk albedo × slab gain
static const Color3B LINE_COLOR   = Color3B(90, 62, 42);    // line albedo × slab gain
static const Color3B STAR_COLOR   = Color3B(96, 66, 42);
static const Color3B RIVER_COLOR  = Color3B(96, 66, 42);
static const Color3B TRAY_COLOR   = Color3B(182, 152, 143); // reference-measured

// Piece colors (Piece.cs BuildProcedural materials)
static const Color3B WOOD_RED    = Color3B(224, 179, 120);  // (0.88,0.70,0.47)
static const Color3B WOOD_BLACK  = Color3B(189, 148, 97);   // (0.74,0.58,0.38)
static const Color3B RIM_RED     = Color3B(158, 41, 31);    // (0.62,0.16,0.12)
static const Color3B RIM_BLACK   = Color3B(41, 38, 36);     // (0.16,0.15,0.14)
static const Color4B GLYPH_RED   = Color4B(173, 31, 20, 255);   // (0.68,0.12,0.08)
static const Color4B GLYPH_BLACK = Color4B(41, 36, 31, 255);    // (0.16,0.14,0.12)
static const Color4B GLYPH_OUTLINE = Color4B(242, 230, 199, 255); // (0.95,0.9,0.78)

// Highlight colors (Board.cs markers)
static const Color3B HINT_DOT   = Color3B(102, 230, 102);  // (0.4,0.9,0.4)
static const Color3B HINT_RING  = Color3B(255, 89, 64);    // (1,0.35,0.25)
static const Color3B SEL_RING   = Color3B(255, 217, 77);   // (1,0.85,0.3)
static const Color3B CHECK_RING = Color3B(255, 51, 38);    // (1,0.2,0.15)
static const Color3B LAST_MARK  = Color3B(89, 191, 255);   // (0.35,0.75,1)
static const Color3B ILLEGAL_COLOR = Color3B(255, 51, 38);

// FX.Burst debris colors
static const Color3B DUST_COLOR   = Color3B(140, 112, 77);  // (0.55,0.44,0.30)
static const Color3B CAP_RED      = Color3B(217, 77, 51);   // (0.85,0.3,0.2)
static const Color3B CAP_BLACK   = Color3B(77, 66, 56);     // (0.3,0.26,0.22)
static const Color3B CELEBRATE_GOLD = Color3B(255, 217, 77); // (1,0.85,0.3) end-of-game burst

// --net-host / --net-join automation (SetNetTestFlags, called from main)
static bool s_autoHost = false, s_autoJoin = false;
static std::string s_bootTag = "boot";
// --verify automation (SetVerifyFlow, called from main)
static bool s_verifyFlow = false;

// Background — sampled from the Godot reference render (greyish studio sky)
static const Color4F BG_COLOR = Color4F(117.0f / 255, 119.0f / 255, 126.0f / 255, 1.0f);

// Untextured unlit meshes must bind an explicit white texture: the unlit
// shader (3D_colorTexture.frag) samples u_texture blindly, and an empty slot
// leaks whatever texture the pipeline bound last — the GLB wood maps made
// the whole board render dark. The engine's getDummyTexture is
// transparent-black in release builds, so load a real 2×2 white PNG. The
// texture must be set BEFORE genMaterial: Mesh::setMaterial replays stored
// textures into the material's passes (the exact path the GLB pieces use).
static Texture2D* s_whiteTex = nullptr;
static Texture2D* WhiteTexture() {
    if (!s_whiteTex)
        s_whiteTex = Director::getInstance()->getTextureCache()->addImage("white.png");
    return s_whiteTex;
}
static void BindWhiteTexture(Sprite3D* node) {
    if (!node) return;
    // Lights must never flip these meshes to the lit pipeline: Sprite3D::draw
    // auto-calls genMaterial(true) whenever a scene light matches the node's
    // light mask, silently swapping the unlit material (with its bound white
    // texture) for an ambient-only lit one that halves every board color.
    // A zero mask keeps the unlit program stable — the lit GLB pieces keep
    // their own default mask (-1) and their lighting.
    node->setLightMask(0);
    if (auto* mesh = node->getMesh())
        if (auto* wt = WhiteTexture())
            mesh->setTexture(wt);
}

static const unsigned short MASK_3D = (unsigned short)CameraFlag::USER1;

// GLB piece target heights (Piece.cs TargetHeight × 1.1): body extension over the
// uniform procedural silhouette (~0.49 world units tall).
static float BodyExtra(PieceType t) {
    switch (t) {
        case PieceType::King:     return 0.55f;  // 1.045 total
        case PieceType::Advisor:  return 0.33f;  // 0.825
        case PieceType::Elephant: return 0.39f;  // 0.88
        case PieceType::Horse:    return 0.44f;  // 0.935
        case PieceType::Chariot:  return 0.44f;
        case PieceType::Cannon:   return 0.44f;
        default:                  return 0.22f;  // soldier: 0.715
    }
}

// ═══════════════ mesh generation ═══════════════

Mesh* GameScene::CreateCylinder(float botR, float topR, float height, int seg) {
    std::vector<float> pos, nrm, uv;
    Mesh::IndexArray idx;
    for (int i = 0; i <= seg; i++) {
        float a = (float)i / seg * 2.0f * 3.14159265f;
        float c = std::cos(a), s = std::sin(a);
        pos.insert(pos.end(), { botR * c, 0.0f, botR * s });
        nrm.insert(nrm.end(), { c, 0.0f, s });
        uv.insert(uv.end(), { (float)i / seg, 0.0f });
        pos.insert(pos.end(), { topR * c, height, topR * s });
        nrm.insert(nrm.end(), { c, 0.0f, s });
        uv.insert(uv.end(), { (float)i / seg, 1.0f });
    }
    for (int i = 0; i < seg; i++) {
        idx.insert(idx.end(), { (unsigned short)(i * 2), (unsigned short)((i + 1) * 2), (unsigned short)(i * 2 + 1) });
        idx.insert(idx.end(), { (unsigned short)(i * 2 + 1), (unsigned short)((i + 1) * 2), (unsigned short)((i + 1) * 2 + 1) });
    }
    int tc = (int)(pos.size() / 3);
    pos.insert(pos.end(), { 0, height, 0 }); nrm.insert(nrm.end(), { 0, 1, 0 }); uv.insert(uv.end(), { .5f, .5f });
    for (int i = 0; i < seg; i++)
        idx.insert(idx.end(), { (unsigned short)tc, (unsigned short)((i + 1) * 2 + 1), (unsigned short)(i * 2 + 1) });
    int bc = (int)(pos.size() / 3);
    pos.insert(pos.end(), { 0, 0, 0 }); nrm.insert(nrm.end(), { 0, -1, 0 }); uv.insert(uv.end(), { .5f, .5f });
    for (int i = 0; i < seg; i++)
        idx.insert(idx.end(), { (unsigned short)bc, (unsigned short)(i * 2), (unsigned short)((i + 1) * 2) });
    return Mesh::create(pos, nrm, uv, idx);
}

Mesh* GameScene::CreateDome(float radius, float squashY, int seg, int rings) {
    std::vector<float> pos, nrm, uv;
    Mesh::IndexArray idx;
    // rings from equator (y=0) to pole (y=radius*squashY)
    for (int r = 0; r <= rings; r++) {
        float phi = (float)r / rings * 1.5707963f; // 0..pi/2
        float y = std::sin(phi) * radius * squashY;
        float rad = std::cos(phi) * radius;
        for (int i = 0; i <= seg; i++) {
            float a = (float)i / seg * 2.0f * 3.14159265f;
            float c = std::cos(a), s = std::sin(a);
            pos.insert(pos.end(), { rad * c, y, rad * s });
            float len = std::sqrt(rad * rad + y * y);
            nrm.insert(nrm.end(), { rad * c / len, y / len, rad * s / len });
            uv.insert(uv.end(), { (float)i / seg, (float)r / rings });
        }
    }
    // quads between rings
    for (int r = 0; r < rings; r++) {
        for (int i = 0; i < seg; i++) {
            int v0 = r * (seg + 1) + i;
            int v1 = r * (seg + 1) + i + 1;
            int v2 = (r + 1) * (seg + 1) + i;
            int v3 = (r + 1) * (seg + 1) + i + 1;
            idx.insert(idx.end(), { (unsigned short)v0, (unsigned short)v1, (unsigned short)v2 });
            idx.insert(idx.end(), { (unsigned short)v1, (unsigned short)v3, (unsigned short)v2 });
        }
    }
    // bottom disc
    int bc = (int)(pos.size() / 3);
    pos.insert(pos.end(), { 0, 0, 0 }); nrm.insert(nrm.end(), { 0, -1, 0 }); uv.insert(uv.end(), { .5f, .5f });
    for (int i = 0; i < seg; i++) {
        int v0 = 0 * (seg + 1) + i;
        int v1 = 0 * (seg + 1) + i + 1;
        idx.insert(idx.end(), { (unsigned short)bc, (unsigned short)v0, (unsigned short)v1 });
    }
    return Mesh::create(pos, nrm, uv, idx);
}

static void PushQuad(std::vector<float>& pos, std::vector<float>& nrm, std::vector<float>& uv,
                     Mesh::IndexArray& idx,
                     float ax, float ay, float az, float bx, float by, float bz,
                     float cx, float cy, float cz, float dx, float dy, float dz,
                     float nx, float ny, float nz) {
    unsigned short base = (unsigned short)(pos.size() / 3);
    pos.insert(pos.end(), { ax, ay, az, bx, by, bz, cx, cy, cz, dx, dy, dz });
    for (int i = 0; i < 4; i++) nrm.insert(nrm.end(), { nx, ny, nz });
    uv.insert(uv.end(), { 0, 0, 1, 0, 1, 1, 0, 1 });
    idx.insert(idx.end(), { base, (unsigned short)(base + 1), (unsigned short)(base + 2),
                            base, (unsigned short)(base + 2), (unsigned short)(base + 3) });
}

// Axis-aligned box centered at the local origin (Godot BoxMesh semantics: Position = center)
Mesh* GameScene::CreateBox(float w, float h, float d) {
    std::vector<float> pos, nrm, uv;
    Mesh::IndexArray idx;
    float x = w / 2, y = h / 2, z = d / 2;
    PushQuad(pos, nrm, uv, idx, -x, y, -z,  x, y, -z,  x, y,  z, -x, y,  z,  0, 1, 0);  // top
    PushQuad(pos, nrm, uv, idx, -x, -y, z,  x, -y, z,  x, -y, -z, -x, -y, -z, 0, -1, 0); // bottom
    PushQuad(pos, nrm, uv, idx,  x, -y, -z, x, -y, z,  x, y, z,  x, y, -z,  1, 0, 0);  // +x
    PushQuad(pos, nrm, uv, idx, -x, -y, z, -x, -y, -z, -x, y, -z, -x, y, z, -1, 0, 0); // -x
    PushQuad(pos, nrm, uv, idx, -x, -y, z,  x, -y, z,  x, y, z, -x, y, z,  0, 0, 1);  // +z
    PushQuad(pos, nrm, uv, idx,  x, -y, -z, -x, -y, -z, -x, y, -z,  x, y, -z, 0, 0, -1); // -z
    return Mesh::create(pos, nrm, uv, idx);
}

Sprite3D* GameScene::MakeBoxNode(float w, float h, float d, const Color3B& color) {
    auto node = Sprite3D::create();
    node->addMesh(CreateBox(w, h, d));
    BindWhiteTexture(node); // pre-material: setMaterial replays it into the pass
    node->genMaterial(false);
    node->setColor(color);
    node->setCameraMask(MASK_3D);
    node->setCullFaceEnabled(false);
    return node;
}

// ═══════════════ init ═══════════════

GameScene* GameScene::create() {
    auto s = new (std::nothrow) GameScene();
    if (s && s->init()) { s->autorelease(); return s; }
    CC_SAFE_DELETE(s);
    return nullptr;
}

bool GameScene::init() {
    if (!Scene::init()) return false;
    scheduleUpdate();

    Director::getInstance()->setClearColor(BG_COLOR);

    BuildCamera();
    BuildBoard();
    BuildBoardLines();
    BuildRiverText();
    BuildTrays();
    BuildMarkers();

    piecesLayer_ = Node::create();
    piecesLayer_->setCameraMask(MASK_3D);
    addChild(piecesLayer_);

    trayLayer_ = Node::create();
    trayLayer_->setCameraMask(MASK_3D);
    addChild(trayLayer_);

    // light rig for the lit (GLB-derived) piece materials — the unlit board and
    // markers ignore lights, so this only shades the real models. The engine's
    // lambert caps MAX_DIRECTIONAL_LIGHT_NUM at 1, so the Godot reference's
    // 0.3 fill light is folded into the ambient instead of a second light.
    auto ambient = AmbientLight::create(Color3B(128, 120, 112));
    ambient->setCameraMask(MASK_3D);
    addChild(ambient);
    auto sun = DirectionLight::create(Vec3(0.32f, -0.85f, 0.42f), Color3B(255, 244, 230));
    sun->setIntensity(1.5f);
    sun->setCameraMask(MASK_3D);
    addChild(sun);

    // 2D status bar — DEFAULT camera (2D ortho), rendered after the 3D pass
    auto visible = Director::getInstance()->getVisibleSize();
    statusLabel_ = Label::createWithSystemFont("选择对局模式", "Microsoft YaHei", 26);
    if (statusLabel_) {
        statusLabel_->setTextColor(Color4B(240, 230, 210, 255));
        statusLabel_->setPosition(Vec2(visible.width / 2, visible.height - 34));
        addChild(statusLabel_, 10);
    }

    // FPS corner (desktop verification aid)
    fpsLabel_ = Label::createWithSystemFont("", "Microsoft YaHei", 18);
    if (fpsLabel_) {
        fpsLabel_->setTextColor(Color4B(160, 160, 150, 255));
        fpsLabel_->setAnchorPoint(Vec2(0.0f, 0.5f));
        fpsLabel_->setPosition(Vec2(14.0f, visible.height - 22.0f));
        addChild(fpsLabel_, 10);
    }

    // HUD check flash (HUD.cs _checkFlash: "将军！", 54pt red, fades + shakes)
    checkFlashLabel_ = Label::createWithSystemFont("将军！", "Microsoft YaHei", 54);
    if (checkFlashLabel_) {
        checkFlashLabel_->setTextColor(Color4B(255, 82, 64, 255));
        checkFlashLabel_->enableOutline(Color4B(50, 0, 0, 216), 4);
        checkFlashBase_ = Vec2(visible.width / 2, visible.height - 170.0f);
        checkFlashLabel_->setPosition(checkFlashBase_);
        checkFlashLabel_->setVisible(false);
        addChild(checkFlashLabel_, 12);
    }

    RefreshAllPieces();
    UpdateStatusLabel();

    BuildStartOverlay();
    BuildNetworkPanel();
    BuildGameButtons();
    BuildEndOverlay();

    // LAN-test automation: self-connect without touch injection
    if (s_autoHost || s_autoJoin) {
        scheduleOnce([this](float) {
            if (netPanel_) netPanel_->setVisible(true);
            if (s_autoHost) {
                net_.host();
                UpdateNetPanelStatus("主机已创建 · 端口 5005 · 等待对手…");
            } else {
                net_.join("127.0.0.1");
                UpdateNetPanelStatus("连接中… 127.0.0.1");
            }
        }, 1.0f, "net-auto");
    }

    // --verify: scripted two-player flow (drives the paths touch injection can't)
    if (s_verifyFlow)
        scheduleOnce([this](float) { RunVerifyFlow(); }, 0.3f, "verify-flow");

    // boot screenshot — visual verification of the running game (capture recipe
    // from the engine guide: utils::captureScreen reads the GL backbuffer)
    scheduleOnce([](float) {
        utils::captureScreen([](bool ok, const std::string& path) {
            CCLOG("captureScreen ok=%d path=%s", ok ? 1 : 0, path.c_str());
        }, "D:/godogen/CocosChess/capture_" + s_bootTag + ".png");
    }, 1.5f, "boot-capture");
    return true;
}

void GameScene::BuildCamera() {
    auto visible = Director::getInstance()->getVisibleSize();
    cam3d_ = Camera::createPerspective(CAM_FOV, visible.width / visible.height, 0.1f, 100.0f);
    cam3d_->setCameraFlag(CameraFlag::USER1);
    cam3d_->setDepth(-1); // render before the default 2D camera so UI stays on top
    cam3d_->setPosition3D(camPos_);
    cam3d_->lookAt(Vec3(0.0f, 0.0f, CAM_TARGET_Z), Vec3(0.0f, 1.0f, 0.0f));
    addChild(cam3d_);
}

void GameScene::BuildBoard() {
    // desk (Godot: BoxMesh 11.5×0.8×12.5 @ y=-0.55, dark wood)
    auto desk = MakeBoxNode(11.5f, 0.8f, 12.5f, DESK_COLOR);
    desk->setPosition3D(Vec3(0.0f, -0.55f, 0.0f));
    addChild(desk);

    // slab (Godot: BoxMesh 9.6×0.32×10.6 @ y=-0.16, light wood)
    auto slab = MakeBoxNode(9.6f, 0.32f, 10.6f, SLAB_COLOR);
    slab->setPosition3D(Vec3(0.0f, -0.16f, 0.0f));
    addChild(slab);

    // plank stripes: 9 per-plank tints over the slab (Board.cs MultiMesh)
    for (int i = 0; i < 9; i++) {
        auto stripe = MakeBoxNode(S * 0.96f, 0.015f, 10.4f, i % 2 == 0 ? SLAB_COLOR : STRIPE_COLOR);
        stripe->setPosition3D(Vec3(-4.0f + i * S, -0.005f, 0.0f));
        addChild(stripe);
    }

    // raised frame around the slab, same dark wood as the desk (Board.cs)
    const float frames[4][4] = {
        { 10.1f, 0.25f,  0.0f, -5.42f },
        { 10.1f, 0.25f,  0.0f,  5.42f },
        { 0.25f, 10.6f, -4.92f, 0.0f },
        { 0.25f, 10.6f,  4.92f, 0.0f },
    };
    for (auto& f : frames) {
        auto frame = MakeBoxNode(f[0], 0.1f, f[1], DESK_COLOR);
        frame->setPosition3D(Vec3(f[2], -0.05f, f[3]));
        addChild(frame);
    }
}

void GameScene::BuildBoardLines() {
    // Godot draws each line as a thin 3D box (len, 0.012, 0.045) — do the same so the
    // grid is depth-tested and correctly occluded by pieces standing on it.
    for (int r = 0; r < 10; r++) { // 10 horizontal lines
        auto line = MakeBoxNode(8 * S, 0.012f, 0.045f, LINE_COLOR);
        line->setPosition3D(Vec3(0.0f, 0.006f, (4.5f - r) * S));
        addChild(line);
    }
    for (int f = 0; f < 9; f++) { // 9 vertical lines, broken at the river except edge files
        float x = (f - 4) * S;
        if (f == 0 || f == 8) {
            auto line = MakeBoxNode(0.045f, 0.012f, 9 * S, LINE_COLOR);
            line->setPosition3D(Vec3(x, 0.006f, 0.0f));
            addChild(line);
        } else {
            auto north = MakeBoxNode(0.045f, 0.012f, 4 * S, LINE_COLOR);
            north->setPosition3D(Vec3(x, 0.006f, 2.5f * S));
            addChild(north);
            auto south = MakeBoxNode(0.045f, 0.012f, 4 * S, LINE_COLOR);
            south->setPosition3D(Vec3(x, 0.006f, -2.5f * S));
            addChild(south);
        }
    }
    // palace diagonals: 4 boxes, yaw ±45° (Board.cs)
    float diagLen = std::sqrt(8.0f) * S;
    const float diags[4][2] = { { 3.5f, 45.0f }, { 3.5f, -45.0f }, { -3.5f, 45.0f }, { -3.5f, -45.0f } };
    for (auto& d : diags) {
        auto line = MakeBoxNode(diagLen, 0.012f, 0.045f, LINE_COLOR);
        line->setRotation3D(Vec3(0.0f, d[1], 0.0f));
        line->setPosition3D(Vec3(0.0f, 0.006f, d[0] * S));
        addChild(line);
    }
    // star marks: 4 small boxes (0.1×0.01×0.1) per point (Board.cs BuildStarMarks)
    auto addStar = [&](int f, int r) {
        for (float dx : { -0.13f, 0.13f }) for (float dz : { -0.13f, 0.13f }) {
            auto dot = MakeBoxNode(0.1f, 0.01f, 0.1f, STAR_COLOR);
            dot->setPosition3D(Vec3((f - 4) * S + dx, 0.007f, (4.5f - r) * S + dz));
            addChild(dot);
        }
    };
    for (int r : { 2, 7 }) for (int f : { 1, 7 }) addStar(f, r);
    for (int r : { 3, 6 }) for (int f : { 0, 2, 4, 6, 8 }) addStar(f, r);
}

void GameScene::BuildRiverText() {
    // Godot: Label3D FontSize 88, PixelSize 0.005, rot -90°X, y=0.012 — reads upright
    // from the red side. A 2D Label scaled to world units under the 3D camera matches.
    auto root = Node::create();
    root->setRotation3D(Vec3(-90.0f, 0.0f, 0.0f));
    root->setPosition3D(Vec3(0.0f, 0.012f, 0.0f));
    addChild(root);
    const char* texts[2] = { "楚 河", "汉 界" };
    float xs[2] = { -2.5f, 2.5f };
    for (int i = 0; i < 2; i++) {
        auto l = Label::createWithSystemFont(texts[i], "Microsoft YaHei", 88);
        if (!l) continue;
        l->setTextColor(Color4B(RIVER_COLOR.r, RIVER_COLOR.g, RIVER_COLOR.b, 255));
        l->enableOutline(Color4B(30, 18, 8, 255), 4);
        l->setScale(0.005f); // 88 px × 0.005 = 0.44 world units tall
        l->setPosition(Vec2(xs[i], 0.0f));
        root->addChild(l);
    }
    root->setCameraMask(MASK_3D); // applies to the labels added above
}

void GameScene::BuildTrays() {
    const float sx[2] = { -6.6f, 6.6f };
    for (int i = 0; i < 2; i++) {
        auto tray = Node::create();
        tray->setPosition3D(Vec3(sx[i], -0.02f, 0.0f));
        auto base = MakeBoxNode(2.2f, 0.12f, 5.6f, TRAY_COLOR);
        base->setPosition3D(Vec3(0.0f, -0.06f, 0.0f));
        tray->addChild(base);
        const float walls[4][4] = {
            { 2.2f, 0.12f,  0.0f, -2.74f },
            { 2.2f, 0.12f,  0.0f,  2.74f },
            { 0.12f, 5.6f, -1.04f, 0.0f },
            { 0.12f, 5.6f,  1.04f, 0.0f },
        };
        for (auto& w : walls) {
            auto wall = MakeBoxNode(w[0], 0.16f, w[1], TRAY_COLOR);
            wall->setPosition3D(Vec3(w[2], 0.0f, w[3]));
            tray->addChild(wall);
        }
        addChild(tray);
        tray->setCameraMask(MASK_3D); // after add: also covers the boxes
    }
}

void GameScene::BuildMarkers() {
    // selection ring (Godot: torus 0.34-0.44, gold glow, pulsing 1+0.08sin(4t))
    selectedRing_ = Sprite3D::create();
    selectedRing_->addMesh(CreateCylinder(0.44f, 0.44f, 0.03f, 40));
    BindWhiteTexture(selectedRing_);
    selectedRing_->genMaterial(false);
    selectedRing_->setColor(SEL_RING);
    selectedRing_->setCameraMask(MASK_3D);
    selectedRing_->setCullFaceEnabled(false);
    selectedRing_->setVisible(false);
    addChild(selectedRing_);

    // check ring (Godot: torus 0.36-0.48, red glow ×2.0, breathing 1+0.16sin(6t))
    checkRing_ = Sprite3D::create();
    checkRing_->addMesh(CreateCylinder(0.48f, 0.48f, 0.03f, 40));
    BindWhiteTexture(checkRing_);
    checkRing_->genMaterial(false);
    checkRing_->setColor(CHECK_RING);
    checkRing_->setCameraMask(MASK_3D);
    checkRing_->setCullFaceEnabled(false);
    checkRing_->setVisible(false);
    addChild(checkRing_);

    // last move: corner brackets at from/to (Board.cs MakeBracket: half=0.42, leg=0.26,
    // th=0.055). Direct scene children, parked under the desk until shown.
    const float half = 0.42f, leg = 0.26f, th = 0.055f;
    auto buildBracket = [this, half, leg, th](std::vector<Sprite3D*>& boxes) {
        for (float sxf : { -1.0f, 1.0f }) for (float szf : { -1.0f, 1.0f }) {
            auto h = MakeBoxNode(leg, 0.014f, th, LAST_MARK);
            h->setPosition3D(Vec3(sxf * (half - leg / 2), -5.0f, szf * half)); // parked
            addChild(h);
            boxes.push_back(h);
            auto v = MakeBoxNode(th, 0.014f, leg, LAST_MARK);
            v->setPosition3D(Vec3(sxf * half, -5.0f, szf * (half - leg / 2)));
            addChild(v);
            boxes.push_back(v);
        }
    };
    buildBracket(lastFromBoxes_);
    buildBracket(lastToBoxes_);

    // pooled hint markers — runtime-created Sprite3Ds don't render under this setup,
    // so everything is created up front and only repositioned later (Godot: dot
    // cyl(R=0.16) green; capture torus(0.3-0.4) red ring). They follow the selection
    // ring's lifecycle: created in place, toggled via setVisible — never "parked".
    for (int i = 0; i < 24; i++) { // lifecycle mirrors the working selection ring
        auto dot = Sprite3D::create();
        dot->addMesh(CreateBox(0.28f, 0.01f, 0.28f)); // box mesh: cylinders at board level
        BindWhiteTexture(dot);                       // proved unreliable in this engine
        dot->genMaterial(false);
        dot->setColor(HINT_DOT);
        dot->setCameraMask(MASK_3D);
        dot->setCullFaceEnabled(false);
        dot->setVisible(false);
        addChild(dot);
        hintDots_.push_back(dot);

        auto ring = Sprite3D::create();
        ring->addMesh(CreateCylinder(0.40f, 0.40f, 0.025f, 36));
        BindWhiteTexture(ring);
        ring->genMaterial(false);
        ring->setColor(HINT_RING);
        ring->setCameraMask(MASK_3D);
        ring->setCullFaceEnabled(false);
        ring->setVisible(false);
        addChild(ring);
        hintRings_.push_back(ring);
    }

    // pooled debris cubes (FX.Burst: 0.14 cubes, up to 16 per burst + landing dust)
    for (int i = 0; i < 80; i++) {
        auto cube = MakeBoxNode(0.14f, 0.14f, 0.14f, DUST_COLOR);
        cube->setPosition3D(Vec3(0.0f, -5.0f, 0.0f)); // parked
        addChild(cube);
        debrisPool_.push_back(cube);
    }

    // illegal-target flash mark (Godot: box 0.55×0.02×0.55, red, fades over 0.45s)
    illegalMark_ = MakeBoxNode(0.55f, 0.02f, 0.55f, ILLEGAL_COLOR);
    illegalMark_->setVisible(false);
    addChild(illegalMark_);

    // desktop hover hint (Board.cs _hoverMark: cyl r=0.13 h=0.02, faint white;
    // a light disc reads as "faint" since these unlit materials have no blend)
    hoverMark_ = Sprite3D::create();
    hoverMark_->addMesh(CreateCylinder(0.13f, 0.13f, 0.02f, 32));
    BindWhiteTexture(hoverMark_);
    hoverMark_->genMaterial(false);
    hoverMark_->setColor(Color3B(236, 222, 196));
    hoverMark_->setCameraMask(MASK_3D);
    hoverMark_->setCullFaceEnabled(false);
    hoverMark_->setVisible(false);
    addChild(hoverMark_);
}

// ═══════════════ pieces ═══════════════

// Read a tools/glb_to_xmodel.py bundle: the mesh of a reference GLB piece
// (already scaled/centered like Godot Piece.BuildFromGlb) plus its base-color
// JPEG. cocos2d-x v4 has no glTF loader, so the flattening happens offline.
// The decoded data is cached per path — 32 pieces share 14 files, so each
// ~1 MB JPEG decodes exactly once.
Sprite3D* GameScene::LoadXModel(const std::string& path) {
    auto it = modelCache_.find(path);
    if (it == modelCache_.end()) {
        Data d = FileUtils::getInstance()->getDataFromFile(path);
        if (d.isNull() || d.getSize() < 22) return nullptr;
        const unsigned char* p = d.getBytes();
        size_t off = 0;
        auto rdU32 = [&]() -> uint32_t {
            uint32_t v; memcpy(&v, p + off, 4); off += 4; return v;
        };
        if (rdU32() != 0x444F4D58) return nullptr; // "XMOD"
        if (rdU32() != 1) return nullptr;           // version
        uint32_t vc = rdU32(), ic = rdU32(), texLen = rdU32();
        off += 2; // texType byte + pad byte (Image sniffs the format itself)
        if (off + (size_t)vc * 32 + (size_t)ic * 2 + texLen > (size_t)d.getSize()) return nullptr;

        XModelData entry;
        entry.pos.resize(vc * 3);
        entry.nrm.resize(vc * 3);
        entry.uv.resize(vc * 2);
        memcpy(entry.pos.data(), p + off, (size_t)vc * 12); off += (size_t)vc * 12;
        memcpy(entry.nrm.data(), p + off, (size_t)vc * 12); off += (size_t)vc * 12;
        memcpy(entry.uv.data(),  p + off, (size_t)vc * 8);  off += (size_t)vc * 8;
        entry.idx.resize(ic);
        memcpy(entry.idx.data(), p + off, (size_t)ic * 2);  off += (size_t)ic * 2;

        if (texLen > 0 && off + texLen <= (size_t)d.getSize()) {
            auto image = new Image();
            if (image->initWithImageData(p + off, texLen)) {
                auto tex = new Texture2D();
                if (tex->initWithImage(image)) {
                    tex->retain(); // cache owns it; every mesh takes its own ref
                    entry.tex = tex;
                }
                tex->release();
            }
            image->release();
        }
        it = modelCache_.emplace(path, std::move(entry)).first;
    }

    const XModelData& m = it->second;
    auto mesh = Mesh::create(m.pos, m.nrm, m.uv, m.idx);
    if (!mesh) return nullptr;
    if (m.tex) mesh->setTexture(m.tex); // diffuse slot; Mesh retains it

    auto node = Sprite3D::create();
    node->addMesh(mesh);
    // lit material: the reference render gets its depth from PBR lighting;
    // the engine's lambert + the scene lights approximate it (normal maps
    // would need tangents, which the GLBs do not carry).
    node->genMaterial(true);
    node->setCullFaceEnabled(false);
    return node;
}

GameScene::PieceRef* GameScene::RefAt(int idx) {
    for (auto& pr : allPieces_)
        if (pr.alive && pr.idx == idx) return &pr;
    return nullptr;
}

Sprite3D* GameScene::CreatePiece3D(int cellValue, float* baseYawOut) {
    bool isRed = cellValue > 0;
    auto type = (PieceType)std::abs(cellValue);
    auto side = isRed ? Side::Red : Side::Black;

    // real GLB-derived model first (Godot Piece.cs prefers the GLB and falls
    // back to procedural). The model textures carry the engraved characters,
    // so this path adds no glyph Label; red rotates to face its player
    // (Piece.BuildFromGlb).
    static const char* typeNames[(int)PieceType::Soldier + 1] = {
        "", "king", "advisor", "elephant", "horse", "chariot", "cannon", "soldier"
    };
    std::string modelPath = StringUtils::format("models/%s_%s.xmodel",
        typeNames[(int)type], isRed ? "red" : "black");
    if (auto node = LoadXModel(modelPath)) {
        node->setColor(Color3B::WHITE); // the baked texture carries the wood colors
        if (baseYawOut) *baseYawOut = isRed ? 180.0f : 0.0f;
        if (isRed) node->setRotation3D(Vec3(0.0f, 180.0f, 0.0f));
        node->setCameraMask(MASK_3D);
        return node;
    }

    Color3B wood = isRed ? WOOD_RED : WOOD_BLACK;
    Color3B rim  = isRed ? RIM_RED : RIM_BLACK;

    // Godot procedural silhouette (uniform 0.49 tall) stretched toward the GLB
    // target heights by extending the body segment.
    float bodyH = 0.16f + BodyExtra(type);
    float yBase = 0.10f;
    float yRim  = yBase + bodyH + 0.02f;
    float yCap  = yRim + 0.02f;
    float yDome = yCap + 0.07f;
    float yGlyph = yDome + 0.32f; // the label quad hangs 0.176 below its position and is
                                  // depth-tested against the dome; +0.32 (dome height 0.126
                                  // + quad 0.176 + margin) floats the full glyph above the dome

    auto piece = Sprite3D::create();

    // 1. base disc: cyl(top=0.31, bot=0.43, h=0.10) @ y=0.05
    auto base = Sprite3D::create();
    base->addMesh(CreateCylinder(0.43f, 0.31f, 0.10f, 32));
    BindWhiteTexture(base); base->genMaterial(false); base->setColor(wood);
    base->setCullFaceEnabled(false);
    base->setPosition3D(Vec3(0, 0.05f, 0));
    piece->addChild(base);

    // 2. body: cyl(top=0.34, bot=0.31, h=0.16+extra)
    auto body = Sprite3D::create();
    body->addMesh(CreateCylinder(0.31f, 0.34f, bodyH, 32));
    BindWhiteTexture(body); body->genMaterial(false); body->setColor(wood);
    body->setCullFaceEnabled(false);
    body->setPosition3D(Vec3(0, yBase + bodyH * 0.5f, 0));
    piece->addChild(body);

    // 3. rim ring: torus(0.26-0.37) approximated with a thin wide cylinder
    auto ring = Sprite3D::create();
    ring->addMesh(CreateCylinder(0.37f, 0.37f, 0.035f, 32));
    BindWhiteTexture(ring); ring->genMaterial(false); ring->setColor(rim);
    ring->setCullFaceEnabled(false);
    ring->setPosition3D(Vec3(0, yRim, 0));
    piece->addChild(ring);

    // 4. cap: cyl(top=0.27, bot=0.34, h=0.07)
    auto cap = Sprite3D::create();
    cap->addMesh(CreateCylinder(0.34f, 0.27f, 0.07f, 32));
    BindWhiteTexture(cap); cap->genMaterial(false); cap->setColor(wood);
    cap->setCullFaceEnabled(false);
    cap->setPosition3D(Vec3(0, yCap, 0));
    piece->addChild(cap);

    // 5. dome: sphere(r=0.30, sy=0.42)
    auto dome = Sprite3D::create();
    dome->addMesh(CreateDome(0.30f, 0.42f, 24, 8));
    BindWhiteTexture(dome); dome->genMaterial(false); dome->setColor(wood);
    dome->setCullFaceEnabled(false);
    dome->setPosition3D(Vec3(0, yDome, 0));
    piece->addChild(dome);

    // 6. Chinese glyph — flat on top, facing up (Godot Label3D rot -90°X, PixelSize 0.004)
    auto label = Label::createWithSystemFont(PieceChar(type, side), "Microsoft YaHei", 44);
    if (label) {
        label->setTextColor(isRed ? GLYPH_RED : GLYPH_BLACK);
        label->enableOutline(GLYPH_OUTLINE, 6);
        label->setRotation3D(Vec3(-90.0f, 0.0f, 0.0f));
        label->setPosition3D(Vec3(0.0f, yGlyph, 0.0f));
        label->setScale(0.004f);
        piece->addChild(label);
    }

    // Orientation: the reference render shows red glyphs upright from the red
    // side and black glyphs rotated 180°; the procedural glyph base reads from
    // the red side, so only black pieces spin (the GLB models are inverted).
    if (!isRed) piece->setRotation3D(Vec3(0.0f, 180.0f, 0.0f));
    if (baseYawOut) *baseYawOut = isRed ? 0.0f : 180.0f;

    piece->setCameraMask(MASK_3D); // applies to the children added above
    return piece;
}

void GameScene::RefreshAllPieces() {
    for (int i = 0; i < 90; i++) {
        if (position_.cells[i] == 0) continue;
        float baseYaw = 0.0f;
        auto node = CreatePiece3D(position_.cells[i], &baseYaw);
        node->setTag(i);
        node->setPosition3D(BoardToWorld(i));
        piecesLayer_->addChild(node);
        PieceRef ref;
        ref.node = node;
        ref.side = position_.cells[i] > 0 ? Side::Red : Side::Black;
        ref.type = (PieceType)std::abs(position_.cells[i]);
        ref.idx = i;
        ref.startIdx = i;
        ref.alive = true;
        ref.baseYaw = baseYaw;
        allPieces_.push_back(ref);
    }
}

void GameScene::ShowMoveHints(const std::vector<Move>& moves, int fromIdx) {
    ClearMoveHints();
    size_t dot = 0, ring = 0;
    for (auto& m : moves) {
        if (m.from != fromIdx) continue;
        Vec3 w = BoardToWorld(m.to);
        bool capture = position_.cells[m.to] != 0;
        Sprite3D* marker = capture
            ? (ring < hintRings_.size() ? hintRings_[ring++] : nullptr)
            : (dot < hintDots_.size() ? hintDots_[dot++] : nullptr);
        if (!marker) continue;
        marker->setPosition3D(Vec3(w.x, 0.035f, w.z)); // Godot: Y=0.035
        marker->setVisible(true);
    }
    if (selectedRing_ && selectedIdx_ >= 0) {
        Vec3 w = BoardToWorld(selectedIdx_);
        selectedRing_->setPosition3D(Vec3(w.x, 0.03f, w.z));
        selectedRing_->setVisible(true);
    }
}

void GameScene::ClearMoveHints() {
    // move home to the implicit origin, out of sight, hidden like the selection ring
    for (auto* m : hintDots_) {
        m->setPosition3D(Vec3::ZERO);
        m->setVisible(false);
    }
    for (auto* m : hintRings_) {
        m->setPosition3D(Vec3::ZERO);
        m->setVisible(false);
    }
    if (selectedRing_) selectedRing_->setVisible(false);
}

void GameScene::ShowLastMove(const Move& m) {
    if (lastFromBoxes_.empty() || lastToBoxes_.empty()) return;
    Vec3 f = BoardToWorld(m.from), t = BoardToWorld(m.to);
    const float half = 0.42f, leg = 0.26f, th = 0.055f;
    const float offsets[8][2] = {
        { -(half - leg / 2), -half }, { -half, -(half - leg / 2) },
        { -(half - leg / 2),  half }, { -half,  (half - leg / 2) },
        {  (half - leg / 2), -half }, {  half, -(half - leg / 2) },
        {  (half - leg / 2),  half }, {  half,  (half - leg / 2) },
    };
    for (int i = 0; i < 8; i++) {
        lastFromBoxes_[i]->setPosition3D(Vec3(f.x + offsets[i][0], 0.008f, f.z + offsets[i][1]));
        lastToBoxes_[i]->setPosition3D(Vec3(t.x + offsets[i][0], 0.008f, t.z + offsets[i][1]));
    }
    (void)th;
}

void GameScene::ShowCheckRing(int kingIdx) {
    if (!checkRing_) return;
    if (kingIdx < 0) { checkRing_->setVisible(false); return; }
    Vec3 w = BoardToWorld(kingIdx);
    checkRing_->setPosition3D(Vec3(w.x, 0.03f, w.z));
    checkRing_->setVisible(true);
}

void GameScene::ShowIllegal(int idx) {
    if (!illegalMark_) return;
    Vec3 w = BoardToWorld(idx);
    illegalMark_->setPosition3D(Vec3(w.x, 0.05f, w.z));
    illegalMark_->setVisible(true);
    illegalT_ = 0.45f;
}

// ═══════════════ picking ═══════════════

Vec3 GameScene::BoardToWorld(int idx) const {
    int f = File(idx), r = Rank(idx);
    return Vec3((f - 4) * S, 0.0f, (4.5f - r) * S);
}

// Screen point → nearest grid index, testing the board plane and the piece-top
// plane, picking the candidate closest to the camera (Godot Game.PickIndex).
int GameScene::PickBoardIndex(const Vec2& screenPos) const {
    if (!cam3d_) return -1;
    // getLocation() is bottom-left origin design points → unprojectGL
    Vec3 w0 = cam3d_->unprojectGL(Vec3(screenPos.x, screenPos.y, 0.0f));
    Vec3 w1 = cam3d_->unprojectGL(Vec3(screenPos.x, screenPos.y, 1.0f));
    Vec3 dir = w1 - w0;
    int bestIdx = -1;
    float bestDist = 1e9f;
    for (float planeY : { 0.0f, 0.45f }) {
        if (std::fabs(dir.y) < 1e-6f) continue;
        float t = (planeY - w0.y) / dir.y;
        if (t <= 0.0f) continue;
        Vec3 hit = w0 + dir * t;
        int f = (int)std::lround(hit.x / S + 4.0f);
        int r = (int)std::lround(4.5f - hit.z / S);
        if (f < 0 || f > 8 || r < 0 || r > 9) continue;
        Vec3 wp = BoardToWorld(Idx(f, r));
        float d2 = (hit.x - wp.x) * (hit.x - wp.x) + (hit.z - wp.z) * (hit.z - wp.z);
        if (d2 < 0.36f && t < bestDist) { bestDist = t; bestIdx = Idx(f, r); }
    }
    return bestIdx;
}

// ═══════════════ interaction ═══════════════

void GameScene::onTouchesBegan(const std::vector<Touch*>& touches, Event*) {
    if (!touches.empty()) {
        touchStart_ = touches[0]->getLocation();
        touchDragging_ = false;
    }
    if (touches.size() >= 2) {
        lastPinchDist_ = touches[0]->getLocation().distance(touches[1]->getLocation());
    }
}

void GameScene::onTouchesMoved(const std::vector<Touch*>& touches, Event*) {
    if (touches.size() >= 2) {
        float d = touches[0]->getLocation().distance(touches[1]->getLocation());
        if (lastPinchDist_ > 1.0f && d > 1.0f) {
            float ratio = lastPinchDist_ / d;
            if (ratio < 0.9f) ratio = 0.9f;
            if (ratio > 1.1f) ratio = 1.1f;
            camDist_ *= ratio; // Godot: Dist *= clamp(lastD/d1, 0.9, 1.1)
        }
        lastPinchDist_ = d;
        touchDragging_ = true;
    } else if (touches.size() == 1) {
        Vec2 pos = touches[0]->getLocation();
        if (pos.distance(touchStart_) > 8.0f) touchDragging_ = true;
        if (touchDragging_) {
            Vec2 delta = touches[0]->getDelta();
            camYaw_ -= delta.x * 0.005f;   // Godot: Yaw -= relX*0.005
            camPitch_ += delta.y * 0.004f; // Godot: Pitch += relY*0.004
        }
    }
}

void GameScene::onTouchesEnded(const std::vector<Touch*>& touches, Event*) {
    if (!touchDragging_ && touches.size() == 1)
        onTap(touches[0]->getLocation());
    if (touches.size() < 2) lastPinchDist_ = 0.0f;
}

void GameScene::onTap(const Vec2& screenPos) {
    if (!gameStarted_ || gameOver_ || animating_ || aiThinking_ || aiMovePending_) return;
    if (mode_ == Mode::VsAI && position_.turn != Side::Red) return;
    if (!IsLocalTurn()) return;
    // Menu items claim one-by-one touches, but AllAtOnce listeners still run —
    // ignore taps that land on the corner buttons so they never pick the board
    if (gameButtons_ && gameButtons_->isVisible()) {
        for (auto* child : gameButtons_->getChildren())
            if (child->getBoundingBox().containsPoint(screenPos)) return;
    }
    int idx = PickBoardIndex(screenPos);

    if (selectedIdx_ >= 0 && idx >= 0) {
        for (auto& m : legalFromSelected_) {
            if (m.to == idx) { ExecuteMove(m); return; }
        }
        if (position_.Own(idx, position_.turn)) { TrySelect(idx); return; } // switch piece
        ShowIllegal(idx); // non-legal target: brief flash, keep the selection
        return;
    }
    if (idx >= 0 && position_.Own(idx, position_.turn)) TrySelect(idx);
    else Deselect();
}

void GameScene::TrySelect(int idx) {
    Deselect();
    if (!position_.Own(idx, position_.turn)) return; // Godot Select: own pieces only
    auto node = piecesLayer_->getChildByTag(idx);
    if (!node) return;
    selectedIdx_ = idx;
    auto all = position_.LegalMoves(position_.turn);
    legalFromSelected_.clear();
    for (auto& m : all)
        if (m.from == idx) legalFromSelected_.push_back(m);
    ShowMoveHints(legalFromSelected_, idx);
    bobT_ = 0.0f;
    hoverSpin_ = 0.0f;
    PlaySound("select");
}

void GameScene::Deselect() {
    if (selectedIdx_ >= 0) {
        if (auto node = piecesLayer_->getChildByTag(selectedIdx_)) {
            node->setPosition3D(BoardToWorld(selectedIdx_));
            if (auto* ref = RefAt(selectedIdx_))
                node->setRotation3D(Vec3(0.0f, ref->baseYaw, 0.0f));
        }
    }
    selectedIdx_ = -1;
    legalFromSelected_.clear();
    ClearMoveHints();
}

// ═══════════════ move flow ═══════════════

void GameScene::ExecuteMove(const Move& m) {
    if (animating_ || gameOver_) return;
    Deselect();
    moveCount_++;

    Node* mover = piecesLayer_->getChildByTag(m.from);
    Node* victim = piecesLayer_->getChildByTag(m.to);
    int captured = 0;
    position_.MakeMove(m, captured);
    moveHistory_.push_back({ m, captured });   // undo stack (Godot Position.History)
    ShowLastMove(m);

    if (victim && captured != 0) {
        for (auto& pr : allPieces_)
            if (pr.node == victim) { pr.alive = false; pr.idx = m.to; break; }
        // fly the captured piece to its tray (arc 2.2, shrink to 0.82 on landing)
        victim->setTag(-1); // drop the board tag so getChildByTag can't find tray pieces
        victim->retain();
        piecesLayer_->removeChild(victim);
        trayLayer_->addChild(victim);
        victim->release();
        activeAnims_.push_back({ victim, victim->getPosition3D(), TraySlot(captured), 0.0f, 2.2f, true });
        PlaySound("capture");
        Vec3 w = BoardToWorld(m.to);
        SpawnDebris(w + Vec3(0.0f, 0.3f, 0.0f), captured > 0 ? CAP_RED : CAP_BLACK, 16, 2.5f, 5.5f);
        // cinematic focus: zoom on the capture point (Game.cs _focusTimer)
        focusPos_ = w;
        focusTimer_ = FOCUS_HOLD;
    }
    if (mover) {
        mover->setTag(m.to);
        activeAnims_.push_back({ mover, mover->getPosition3D(), BoardToWorld(m.to), 0.0f,
                                 victim ? 0.9f : 0.55f, false });
        animating_ = true;
    }
    // sync to the remote peer (Godot RpcMove)
    if ((mode_ == Mode::OnlineHost || mode_ == Mode::OnlineGuest) && net_.connected())
        net_.sendMove(m.from, m.to);
    UpdateStatusLabel();
}

Vec3 GameScene::TraySlot(int capturedValue) {
    int n = capturedValue > 0 ? redCaptured_++ : blackCaptured_++;
    float x = capturedValue > 0 ? -6.6f : 6.6f; // red casualties left, black right
    float z = 2.2f - (n % 5) * 1.1f;
    float y = 0.02f + (n / 5) * 0.55f;
    return Vec3(x, y, z);
}

void GameScene::StartAITurn() {
    if (aiThinking_) return;
    aiThinking_ = true;
    aiGenAtStart_ = aiGen_;   // ResetMatch bumps aiGen_; stale results are dropped
    UpdateStatusLabel();
    Position snapshot = position_;
    Side aiSide = position_.turn;
    aiFuture_ = std::async(std::launch::async, [snapshot, aiSide]() -> Move {
        AIPlayer ai(aiSide);
        int depth = 0; long long nodes = 0;
        return ai.Search(snapshot, 900, depth, nodes); // Godot: 900ms cap
    });
    StartLoop("ai_thinking");
}

void GameScene::FinishMove() {
    animating_ = false;
    Side toMove = position_.turn;
    if (position_.IsCheckmate(toMove) || position_.IsStalemate(toMove)) {
        gameOver_ = true;
        Side loser = toMove;
        Side winner = (loser == Side::Red) ? Side::Black : Side::Red;
        bool mate = position_.InCheck(toMove);
        ShowCheckRing(mate ? position_.KingIdx(toMove) : -1);
        ShowEnd(winner, mate);
        // celebration burst over the defeated king (Game.cs FinishMove)
        int lk = position_.KingIdx(loser);
        if (lk >= 0)
            SpawnDebris(BoardToWorld(lk) + Vec3(0.0f, 0.6f, 0.0f), CELEBRATE_GOLD, 42, 3.0f, 6.5f);
        PlaySound(mate ? "checkmate" : "stalemate");
        // Godot: victory/defeat jingle 0.8s after the end sound, relative to the local side
        pendingEndSound_ = (winner == LocalSide()) ? 1 : 2;
        endSoundDelay_ = 0.8f;
        return;
    }
    bool inCheck = position_.InCheck(toMove);
    ShowCheckRing(inCheck ? position_.KingIdx(toMove) : -1);
    if (inCheck) { ShowCheckFlash(); PlaySound("check"); }
    UpdateStatusLabel();
    if (mode_ == Mode::VsAI && position_.turn == Side::Black) StartAITurn();
}

// ═══════════════ frame update ═══════════════

void GameScene::update(float dt) {
    pulseT_ += dt;

    // network: drain peer messages on the game thread (never from the socket thread)
    while (net_.hasMessages()) {
        NetPlayer::Message msg = net_.popMessage();
        switch (msg.type) {
        case NetPlayer::Message::Connected:
            if (netPanel_) netPanel_->setVisible(false);
            if (startOverlay_ && startOverlay_->isVisible())
                startOverlay_->setVisible(false);
            ChooseMode(net_.role() == NetPlayer::Role::Host ? Mode::OnlineHost
                                                            : Mode::OnlineGuest);
            if (s_autoHost) {
                // play the first move so the guest board provably syncs
                scheduleOnce([this](float) {
                    Move want{ Idx(4, 3), Idx(4, 4) }; // 中兵前进一步
                    bool ok = false;
                    for (auto& lm : position_.LegalMoves(Side::Red))
                        if (lm == want) { ok = true; break; }
                    if (ok) ExecuteMove(want);
                    scheduleOnce([this](float) { DumpNetState(); }, 2.5f, "net-dump-h");
                }, 0.8f, "net-first-move");
            } else if (s_autoJoin) {
                scheduleOnce([this](float) { DumpNetState(); }, 6.0f, "net-dump-g");
            }
            break;
        case NetPlayer::Message::Move: {
            // only apply when it is the remote player's turn (Godot RpcMove guard)
            bool remoteRed = (mode_ == Mode::OnlineGuest);
            bool remoteBlack = (mode_ == Mode::OnlineHost);
            if (remoteRed && position_.turn == Side::Red) ExecuteMove(Move{ msg.a, msg.b });
            else if (remoteBlack && position_.turn == Side::Black) ExecuteMove(Move{ msg.a, msg.b });
            break;
        }
        case NetPlayer::Message::Restart:
            ResetMatch();
            ChooseMode(mode_);
            break;
        case NetPlayer::Message::Leave:
        case NetPlayer::Message::Disconnected:
            HandleOpponentLeft();
            break;
        case NetPlayer::Message::ConnectFailed:
            UpdateNetPanelStatus("连接失败 — 检查 IP 或主机未就绪");
            break;
        }
    }

    // AI result polling: stop the loop, then apply after a 0.4s delay at rest (Game.cs)
    if (aiThinking_ && aiFuture_.valid()) {
        if (aiFuture_.wait_for(std::chrono::seconds(0)) == std::future_status::ready) {
            Move m = aiFuture_.get();
            aiThinking_ = false;
            StopLoop();
            if (aiGenAtStart_ != aiGen_) {
                aiMovePending_ = false; // board was reset while searching — drop it
            } else {
                aiMovePending_ = true;
                pendingAiMove_ = m;
                aiApplyDelay_ = 0.4f;
            }
        }
    }
    if (aiMovePending_ && !animating_ && !gameOver_) {
        aiApplyDelay_ -= dt;
        if (aiApplyDelay_ <= 0.0f) {
            aiMovePending_ = false;
            auto legal = position_.LegalMoves(position_.turn);
            if (!legal.empty()) {
                bool ok = false;
                for (auto& lm : legal) if (lm == pendingAiMove_) { ok = true; break; }
                ExecuteMove(ok ? pendingAiMove_ : legal[0]); // never stall
            }
        }
    }
    if (gameOver_ && pendingEndSound_ != 0) {
        endSoundDelay_ -= dt;
        if (endSoundDelay_ <= 0.0f) {
            PlaySound(pendingEndSound_ == 1 ? "victory" : "defeat");
            pendingEndSound_ = 0;
        }
    }

    UpdateAnimations(dt);
    UpdateSelectedHover(dt);
    UpdateCamera(dt);

    // breathing pulses (Board.cs _Process)
    if (selectedRing_ && selectedRing_->isVisible())
        selectedRing_->setScale(1.0f + 0.08f * std::sin(pulseT_ * 4.0f));
    if (checkRing_ && checkRing_->isVisible())
        checkRing_->setScale(1.0f + 0.16f * std::sin(pulseT_ * 6.0f));
    float dotS = 1.0f + 0.12f * std::sin(pulseT_ * 5.0f);
    for (auto* m : hintDots_) if (m->isVisible()) m->setScale(dotS);
    for (auto* m : hintRings_) if (m->isVisible()) m->setScale(dotS);

    // illegal flash fade (Godot: 0.45s, scale grows as alpha falls)
    if (illegalT_ > 0.0f && illegalMark_ && illegalMark_->isVisible()) {
        illegalT_ -= dt;
        if (illegalT_ <= 0.0f) {
            illegalMark_->setVisible(false);
        } else {
            float a = illegalT_ / 0.45f;
            illegalMark_->setScale(1.0f + 0.25f * (1.0f - a));
        }
    }

    // check flash: 1.6s fade + decaying shake (HUD.FlashCheck / UIAnimator.Shake)
    if (checkFlashT_ > 0.0f && checkFlashLabel_) {
        checkFlashT_ -= dt;
        if (checkFlashT_ <= 0.0f) {
            checkFlashLabel_->setVisible(false);
        } else {
            float a = std::min(1.0f, checkFlashT_ / 0.4f); // hold, then fade (HUD._Process)
            checkFlashLabel_->setOpacity((GLubyte)(255.0f * a));
            if (checkShakeT_ > 0.0f) {
                checkShakeT_ -= dt;
                float decay = checkShakeT_ / 0.35f;
                float ix = (((float)(std::rand() % 1000)) / 1000.0f - 0.5f) * 10.0f * decay;
                float iy = (((float)(std::rand() % 1000)) / 1000.0f - 0.5f) * 10.0f * decay;
                checkFlashLabel_->setPosition(checkFlashBase_ + Vec2(ix, iy));
            } else {
                checkFlashLabel_->setPosition(checkFlashBase_);
            }
        }
    }

    // FPS corner
    if (fpsLabel_) {
        fpsAccum_ += dt;
        fpsFrames_++;
        if (fpsAccum_ >= 0.5f) {
            fpsLabel_->setString(StringUtils::format("%d FPS", (int)(fpsFrames_ / fpsAccum_ + 0.5f)));
            fpsAccum_ = 0.0f;
            fpsFrames_ = 0;
        }
    }

    // debris physics (FX.Burst: up-hemisphere spread 90°, gravity -12, lifetime 0.7s)
    for (int i = (int)activeDebris_.size() - 1; i >= 0; i--) {
        auto& d = activeDebrisState_[i];
        d.ttl -= dt;
        d.vel.y -= 12.0f * dt;
        auto* node = activeDebris_[i];
        node->setPosition3D(node->getPosition3D() + d.vel * dt);
        if (d.ttl <= 0.0f) {
            node->setPosition3D(Vec3(0.0f, -5.0f, 0.0f)); // park back in the pool
            activeDebris_.erase(activeDebris_.begin() + i);
            activeDebrisState_.erase(activeDebrisState_.begin() + i);
            debrisPool_.push_back(node);
        }
    }
}

void GameScene::UpdateAnimations(float dt) {
    for (int i = (int)activeAnims_.size() - 1; i >= 0; i--) {
        auto& a = activeAnims_[i];
        a.t += dt / 0.45f;
        if (a.t >= 1.0f) {
            a.node->setPosition3D(a.to);
            bool wasMover = !a.toTray;
            if (a.toTray) a.node->setScale(0.82f); // Godot: captured pieces shrink in the tray
            activeAnims_.erase(activeAnims_.begin() + i);
            if (wasMover) {
                // landing dust + move sound, then post-move logic (Game.cs _Process)
                SpawnDebris(a.to + Vec3(0.0f, 0.05f, 0.0f), DUST_COLOR, 8, 0.8f, 1.8f);
                PlaySound("move");
                FinishMove();
            }
            continue;
        }
        float k = a.t;
        float arc = std::sin(3.14159265f * k) * a.arcHeight;
        Vec3 p(
            a.from.x + (a.to.x - a.from.x) * k,
            a.from.y + (a.to.y - a.from.y) * k + arc,
            a.from.z + (a.to.z - a.from.z) * k);
        a.node->setPosition3D(p);
    }
}

void GameScene::UpdateSelectedHover(float dt) {
    if (selectedIdx_ < 0 || animating_) return;
    auto node = piecesLayer_->getChildByTag(selectedIdx_);
    if (!node) return;
    // Godot Piece Selected state: y = 0.32+0.04sin(bob*4), spin 1.4 rad/s
    bobT_ += dt;
    hoverSpin_ += dt * 1.4f;
    Vec3 p = node->getPosition3D();
    float baseYaw = 0.0f;
    if (auto* ref = RefAt(selectedIdx_)) baseYaw = ref->baseYaw;
    node->setPosition3D(Vec3(p.x, 0.32f + 0.04f * std::sin(bobT_ * 4.0f), p.z));
    node->setRotation3D(Vec3(0.0f, baseYaw + hoverSpin_ * 180.0f / 3.14159265f, 0.0f));
}

void GameScene::UpdateCamera(float dt) {
    // Godot clamps + smooth lerp (k = dt*6)
    if (camPitch_ < 0.45f) camPitch_ = 0.45f;
    if (camPitch_ > 1.35f) camPitch_ = 1.35f;
    if (camDist_ < 7.0f) camDist_ = 7.0f;
    if (camDist_ > 20.0f) camDist_ = 20.0f;
    if (!cam3d_) return;
    // cinematic capture focus: hold the target on the capture point at close
    // range, then ease back to the orbit target (Game.cs _focusTimer)
    Vec3 target(0.0f, 0.0f, CAM_TARGET_Z);
    float effDist = camDist_;
    if (focusTimer_ > 0.0f) {
        focusTimer_ -= dt;
        target = focusPos_;
        effDist = FOCUS_DIST;
    }
    float cp = std::cos(camPitch_), sp = std::sin(camPitch_);
    Vec3 off(std::sin(camYaw_) * cp * effDist, sp * effDist, std::cos(camYaw_) * cp * effDist);
    Vec3 desired = target + off;
    float k = dt * CAM_EASE;
    if (k > 1.0f) k = 1.0f;
    camPos_.x += (desired.x - camPos_.x) * k;
    camPos_.y += (desired.y - camPos_.y) * k;
    camPos_.z += (desired.z - camPos_.z) * k;
    cam3d_->setPosition3D(camPos_);
    cam3d_->lookAt(target, Vec3(0.0f, 1.0f, 0.0f));
}

// ═══════════════ debris ═══════════════

void GameScene::SpawnDebris(const Vec3& pos, const Color3B& color, int count, float vmin, float vmax) {
    static std::mt19937 rng(20260831);
    for (int i = 0; i < count; i++) {
        if (debrisPool_.empty()) break; // pool exhausted — oldest debris just keeps flying
        auto cube = debrisPool_.back();
        debrisPool_.pop_back();
        cube->setColor(color);
        // scale 0.5..1.1 (Godot ScaleAmountMin/Max)
        std::uniform_real_distribution<float> sc(0.5f, 1.1f);
        cube->setScale(sc(rng));
        cube->setPosition3D(pos);
        activeDebris_.push_back(cube);
        // direction: up with 90° spread
        std::uniform_real_distribution<float> az(0.0f, 6.2831853f);
        std::uniform_real_distribution<float> pol(0.0f, 0.7853982f);
        std::uniform_real_distribution<float> spd(vmin, vmax);
        float phi = pol(rng), theta = az(rng), speed = spd(rng);
        Vec3 vel(std::sin(phi) * std::cos(theta), std::cos(phi), std::sin(phi) * std::sin(theta));
        activeDebrisState_.push_back({ vel * speed, 0.7f });
    }
}

// ═══════════════ game end / status ═══════════════

void GameScene::UpdateStatusLabel() {
    if (!statusLabel_) return;
    statusLabel_->setVisible(gameStarted_); // the start overlay carries its own copy
    if (!gameStarted_) return;
    if (gameOver_) { statusLabel_->setString("对局结束"); return; }
    bool red = (position_.turn == Side::Red);
    const char* side = red ? "红方" : "黑方";
    char buf[128];
    if (aiThinking_ || aiMovePending_ || (mode_ == Mode::VsAI && !red))
        snprintf(buf, sizeof(buf), "第 %d 手 · 黑方思考中…", moveCount_ + 1);
    else if (IsLocalTurn())
        snprintf(buf, sizeof(buf), "第 %d 手 · %s行棋", moveCount_ + 1, side);
    else
        snprintf(buf, sizeof(buf), "第 %d 手 · 等待%s行棋", moveCount_ + 1, side);
    statusLabel_->setString(buf);
}

bool GameScene::IsLocalTurn() const {
    if (mode_ != Mode::OnlineHost && mode_ != Mode::OnlineGuest) return true;
    bool localRed = (mode_ == Mode::OnlineHost); // host takes red (NetworkPanel)
    return (localRed && position_.turn == Side::Red) ||
           (!localRed && position_.turn == Side::Black);
}

Side GameScene::LocalSide() const {
    return (mode_ == Mode::OnlineGuest) ? Side::Black : Side::Red;
}

void GameScene::ShowCheckFlash() {
    if (!checkFlashLabel_) return;
    checkFlashLabel_->setOpacity(255);
    checkFlashLabel_->setPosition(checkFlashBase_);
    checkFlashLabel_->setVisible(true);
    checkFlashT_ = 1.6f;
    checkShakeT_ = 0.35f;
}

void GameScene::HideLastMove() {
    // re-park the brackets under the desk where they were built
    for (auto* b : lastFromBoxes_)
        if (b) b->setPosition3D(Vec3(b->getPosition3D().x, -5.0f, b->getPosition3D().z));
    for (auto* b : lastToBoxes_)
        if (b) b->setPosition3D(Vec3(b->getPosition3D().x, -5.0f, b->getPosition3D().z));
}

void GameScene::ShowEnd(Side winner, bool mate) {
    if (!endOverlay_) return;
    if (endTitleLabel_) {
        endTitleLabel_->setString(winner == Side::Red ? "红方胜利！" : "黑方胜利！");
        endTitleLabel_->setTextColor(winner == Side::Red ? Color4B(255, 128, 102, 255)
                                                         : Color4B(242, 212, 140, 255));
    }
    if (endReasonLabel_)
        endReasonLabel_->setString(mate ? "绝杀 — 被将死" : "困毙 — 无子可动");
    endOverlay_->stopAllActions();
    endOverlay_->setOpacity(0);
    endOverlay_->setVisible(true);
    endOverlay_->runAction(FadeIn::create(0.4f));
    if (endTitleLabel_) { // SlideIn: drop from -60 with fade (UIAnimator.SlideIn)
        Vec2 pos = endTitleLabel_->getPosition();
        endTitleLabel_->stopAllActions();
        endTitleLabel_->setOpacity(0);
        endTitleLabel_->setPosition(pos + Vec2(0.0f, -60.0f));
        endTitleLabel_->runAction(Spawn::create(FadeIn::create(0.5f),
            MoveBy::create(0.5f, Vec2(0.0f, 60.0f)), nullptr));
    }
    UpdateStatusLabel();
}

// ═══════════════ UI overlays (HUD.cs) ═══════════════

// text-only menu button: MenuItemLabel over a styled system-font label
// (procedural-first — no button textures anywhere in this game)
static MenuItemLabel* MakeMenuItem(const char* text, int fontSize, const ccMenuCallback& cb) {
    auto l = Label::createWithSystemFont(text, "Microsoft YaHei", (float)fontSize);
    if (l) {
        l->setTextColor(Color4B(237, 224, 204, 255));    // HUD Cream (0.93,0.88,0.80)
        l->enableOutline(Color4B(30, 20, 10, 200), 2);
    }
    return MenuItemLabel::create(l, cb);
}

void GameScene::BuildStartOverlay() {
    auto visible = Director::getInstance()->getVisibleSize();
    startOverlay_ = LayerColor::create(Color4B(0, 0, 0, 174), visible.width, visible.height);
    if (!startOverlay_) return;
    addChild(startOverlay_, 20);

    auto title = Label::createWithSystemFont("3D 中国象棋", "Microsoft YaHei", 64);
    if (title) {
        title->setTextColor(Color4B(255, 224, 158, 255)); // HUD Gold (1,0.88,0.62)
        title->enableOutline(Color4B(20, 10, 0, 190), 4);
        title->setPosition(Vec2(visible.width / 2, visible.height - 110.0f));
        startOverlay_->addChild(title);
    }
    auto sub = Label::createWithSystemFont("选择对局模式", "Microsoft YaHei", 24);
    if (sub) {
        sub->setTextColor(Color4B(237, 224, 204, 255));
        sub->setPosition(Vec2(visible.width / 2, visible.height - 158.0f));
        startOverlay_->addChild(sub);
    }
    auto menu = Menu::create(
        MakeMenuItem("人机对弈（执红先行）", 26, [this](Ref*) { ChooseMode(Mode::VsAI); }),
        MakeMenuItem("双人对弈（同屏轮流）", 26, [this](Ref*) { ChooseMode(Mode::TwoPlayers); }),
        MakeMenuItem("联机对战", 26, [this](Ref*) {
            if (netPanel_) { netPanel_->setVisible(true); UpdateNetPanelStatus("做主机，或输入主机 IP 加入"); }
        }),
        nullptr);
    if (menu) {
        menu->setPosition(Vec2(visible.width / 2, visible.height / 2 - 40.0f));
        menu->alignItemsVerticallyWithPadding(16.0f);
        startOverlay_->addChild(menu);
    }
    auto hint = Label::createWithSystemFont(
        "单指点选 · 拖动旋转 · 双指缩放    U 悔棋 · R 再来一局", "Microsoft YaHei", 20);
    if (hint) {
        hint->setTextColor(Color4B(199, 181, 153, 255));
        hint->setPosition(Vec2(visible.width / 2, 62.0f));
        startOverlay_->addChild(hint);
    }
}

void GameScene::BuildNetworkPanel() {
    auto visible = Director::getInstance()->getVisibleSize();
    netPanel_ = LayerColor::create(Color4B(0, 0, 0, 200), visible.width, visible.height);
    if (!netPanel_) return;
    netPanel_->setVisible(false);
    addChild(netPanel_, 22);

    auto title = Label::createWithSystemFont("联机对战", "Microsoft YaHei", 36);
    if (title) {
        title->setTextColor(Color4B(255, 224, 158, 255));
        title->enableOutline(Color4B(20, 10, 0, 190), 3);
        title->setPosition(Vec2(visible.width / 2, visible.height - 180.0f));
        netPanel_->addChild(title);
    }
    auto menu = Menu::create(
        MakeMenuItem("做主机（等待对手加入）", 24, [this](Ref*) {
            if (net_.role() != NetPlayer::Role::None) return;
            if (net_.host()) UpdateNetPanelStatus("主机已创建 · 端口 5005 · 等待对手…");
        }),
        MakeMenuItem("加入主机", 24, [this](Ref*) {
            if (net_.role() != NetPlayer::Role::None) return;
            if (net_.join(ipBuffer_)) UpdateNetPanelStatus("连接中… " + ipBuffer_);
        }),
        MakeMenuItem("返回", 20, [this](Ref*) {
            netPanel_->setVisible(false);
            ipEditing_ = false;
        }),
        nullptr);
    if (menu) {
        menu->setPosition(Vec2(visible.width / 2, 360.0f));
        menu->alignItemsVerticallyWithPadding(44.0f);
        netPanel_->addChild(menu);
    }
    ipLabel_ = Label::createWithSystemFont("", "Microsoft YaHei", 22);
    if (ipLabel_) {
        ipLabel_->setTextColor(Color4B(160, 210, 255, 255));
        ipLabel_->setPosition(Vec2(visible.width / 2, 470.0f));
        netPanel_->addChild(ipLabel_);
        RefreshIpLabel();
    }
    netStatusLabel_ = Label::createWithSystemFont("", "Microsoft YaHei", 20);
    if (netStatusLabel_) {
        netStatusLabel_->setTextColor(Color4B(200, 214, 240, 255));
        netStatusLabel_->setPosition(Vec2(visible.width / 2, 150.0f));
        netPanel_->addChild(netStatusLabel_);
    }
}

void GameScene::UpdateNetPanelStatus(const std::string& text) {
    if (netStatusLabel_) netStatusLabel_->setString(text);
}

void GameScene::RefreshIpLabel() {
    if (!ipLabel_) return;
    ipLabel_->setString(StringUtils::format("主机 IP：%s%s", ipBuffer_.c_str(),
        ipEditing_ ? "  （输入中，回车结束）" : "  （按 I 编辑）"));
}

void GameScene::BuildEndOverlay() {
    auto visible = Director::getInstance()->getVisibleSize();
    endOverlay_ = LayerColor::create(Color4B(0, 0, 0, 158), visible.width, visible.height);
    if (!endOverlay_) return;
    endOverlay_->setVisible(false);
    addChild(endOverlay_, 25);

    endTitleLabel_ = Label::createWithSystemFont("", "Microsoft YaHei", 62);
    if (endTitleLabel_) {
        endTitleLabel_->setTextColor(Color4B(255, 230, 115, 255));
        endTitleLabel_->enableOutline(Color4B(20, 10, 0, 200), 4);
        endTitleLabel_->setPosition(Vec2(visible.width / 2, visible.height / 2 + 96.0f));
        endOverlay_->addChild(endTitleLabel_);
    }
    endReasonLabel_ = Label::createWithSystemFont("", "Microsoft YaHei", 26);
    if (endReasonLabel_) {
        endReasonLabel_->setTextColor(Color4B(237, 224, 204, 255));
        endReasonLabel_->setPosition(Vec2(visible.width / 2, visible.height / 2 + 36.0f));
        endOverlay_->addChild(endReasonLabel_);
    }
    auto menu = Menu::create(
        MakeMenuItem("再来一局 (R)", 28, [this](Ref*) { RematchPressed(); }),
        MakeMenuItem("返回菜单", 24, [this](Ref*) { MenuPressed(); }),
        nullptr);
    if (menu) {
        menu->setPosition(Vec2(visible.width / 2, visible.height / 2 - 70.0f));
        menu->alignItemsHorizontallyWithPadding(36.0f);
        endOverlay_->addChild(menu);
    }
}

void GameScene::BuildGameButtons() {
    auto visible = Director::getInstance()->getVisibleSize();
    auto* undo = MakeMenuItem("悔棋 (U)", 22, [this](Ref*) { UndoGame(); });
    auto* menuBtn = MakeMenuItem("菜单", 22, [this](Ref*) { MenuPressed(); });
    auto* reset = MakeMenuItem("再来一局", 22, [this](Ref*) { RematchPressed(); });
    gameButtons_ = Menu::create(undo, menuBtn, reset, nullptr);
    if (!gameButtons_) return;
    // Godot HUD anchors: undo bottom-left, menu top-right, rematch bottom-right
    undo->setPosition(Vec2(-540.0f, -299.0f));
    menuBtn->setPosition(Vec2(466.0f, -309.0f));
    reset->setPosition(Vec2(540.0f, -299.0f));
    gameButtons_->setPosition(Vec2(visible.width / 2, visible.height / 2));
    gameButtons_->setVisible(false);
    addChild(gameButtons_, 15);
}

// ═══════════════ match lifecycle (Game.cs ChooseMode/Rematch/Menu/Undo) ═══════════════

void GameScene::ChooseMode(Mode m) {
    mode_ = m;
    gameStarted_ = true;
    if (startOverlay_ && startOverlay_->isVisible()) {
        startOverlay_->runAction(Sequence::create(
            FadeOut::create(0.2f),
            CallFunc::create([this]() {
                if (startOverlay_) { startOverlay_->setVisible(false); startOverlay_->setOpacity(255); }
            }), nullptr));
    }
    if (netPanel_ && netPanel_->isVisible()) netPanel_->setVisible(false);
    if (gameButtons_ && !gameButtons_->isVisible()) gameButtons_->setVisible(true);
    PlaySound("game_start");
    UpdateStatusLabel();
}

void GameScene::ResetMatch() {
    aiGen_++;                      // any in-flight AI result is stale now
    aiThinking_ = false;
    aiMovePending_ = false;
    StopLoop();
    activeAnims_.clear();
    animating_ = false;
    position_.Reset();
    moveHistory_.clear();
    moveCount_ = 0;
    redCaptured_ = 0;
    blackCaptured_ = 0;
    for (auto& pr : allPieces_) {
        pr.alive = true;
        pr.idx = pr.startIdx;
        if (!pr.node) continue;
        if (pr.node->getParent() != piecesLayer_) { // tray victims come home
            pr.node->retain();
            pr.node->removeFromParent();
            piecesLayer_->addChild(pr.node);
            pr.node->release();
        }
        pr.node->setTag(pr.startIdx);
        pr.node->setPosition3D(BoardToWorld(pr.startIdx));
        pr.node->setScale(1.0f);
        pr.node->setRotation3D(Vec3(0.0f, pr.baseYaw, 0.0f));
    }
    selectedIdx_ = -1;
    legalFromSelected_.clear();
    ClearMoveHints();
    HideLastMove();
    ShowCheckRing(-1);
    if (illegalMark_) illegalMark_->setVisible(false);
    if (endOverlay_) endOverlay_->setVisible(false);
    if (checkFlashLabel_) checkFlashLabel_->setVisible(false);
    gameOver_ = false;
    pendingEndSound_ = 0;
    focusTimer_ = 0.0f;
    UpdateStatusLabel();
}

void GameScene::MenuPressed() {
    if (net_.role() != NetPlayer::Role::None) net_.close();
    ResetMatch();
    gameStarted_ = false;
    mode_ = Mode::VsAI;
    if (gameButtons_) gameButtons_->setVisible(false);
    if (netPanel_) netPanel_->setVisible(false);
    if (startOverlay_) { startOverlay_->setVisible(true); startOverlay_->setOpacity(255); }
    UpdateStatusLabel();
}

void GameScene::RematchPressed() {
    if (net_.connected()) net_.sendRestart();
    ResetMatch();
    ChooseMode(mode_); // instant rematch in the same mode (Godot AutoStart pattern)
}

void GameScene::UndoGame() {
    if (!gameStarted_ || gameOver_ || animating_ || aiThinking_ || aiMovePending_) return;
    if (mode_ == Mode::OnlineHost || mode_ == Mode::OnlineGuest) return; // no undo online
    if (moveHistory_.empty()) return;
    // vs AI: back out both plies when it is your turn again, else one (Game.cs Undo)
    int plies = (mode_ == Mode::VsAI && position_.turn == Side::Red &&
                 moveHistory_.size() >= 2) ? 2 : 1;
    for (int i = 0; i < plies && !moveHistory_.empty(); i++) {
        HistEntry h = moveHistory_.back();
        moveHistory_.pop_back();
        if (auto mover = piecesLayer_->getChildByTag(h.m.to)) {
            mover->setTag(h.m.from);
            mover->setPosition3D(BoardToWorld(h.m.from));
        }
        position_.UndoMove(h.m, h.captured);
        if (h.captured != 0) {
            // revive the tray victim (Godot: find !Alive && Index == m.To)
            for (auto& pr : allPieces_) {
                if (pr.alive || !pr.node || pr.idx != h.m.to) continue;
                int sign = (pr.side == Side::Red) ? 1 : -1;
                if (sign * (int)pr.type != h.captured) continue;
                pr.alive = true;
                pr.node->retain();
                pr.node->removeFromParent(); // out of the tray layer
                piecesLayer_->addChild(pr.node);
                pr.node->release();
                pr.node->setTag(h.m.to);
                pr.node->setPosition3D(BoardToWorld(h.m.to));
                pr.node->setScale(1.0f);
                if (pr.side == Side::Red) redCaptured_--; else blackCaptured_--;
                break;
            }
        }
    }
    moveCount_ = (int)moveHistory_.size();
    selectedIdx_ = -1;
    legalFromSelected_.clear();
    ClearMoveHints();
    if (!moveHistory_.empty()) ShowLastMove(moveHistory_.back().m);
    else HideLastMove();
    Side t = position_.turn;
    ShowCheckRing(position_.InCheck(t) ? position_.KingIdx(t) : -1);
    UpdateStatusLabel();
    PlaySound("undo");
    if (mode_ == Mode::VsAI && position_.turn == Side::Black) StartAITurn();
}

void GameScene::HandleOpponentLeft() {
    if (mode_ != Mode::OnlineHost && mode_ != Mode::OnlineGuest) return;
    if (!gameStarted_ || gameOver_) return;
    gameOver_ = true;
    ShowCheckRing(-1);
    ShowEnd(LocalSide(), false); // the local side wins when the opponent leaves
    PlaySound("victory");
    UpdateNetPanelStatus("对手已断开");
}

// ═══════════════ net-test diagnostics ═══════════════

void GameScene::SetNetTestFlags(bool autoHost, bool autoJoin, const char* tag) {
    s_autoHost = autoHost;
    s_autoJoin = autoJoin;
    if (tag) s_bootTag = tag;
}

void GameScene::DumpNetState() {
    std::string path = StringUtils::format("D:/godogen/CocosChess/net_state_%s.txt",
                                           s_bootTag.c_str());
    std::ofstream f(path);
    if (!f) return;
    for (int i = 0; i < 90; i++) f << position_.cells[i] << (i == 89 ? '\n' : ' ');
    f << "turn " << (position_.turn == Side::Red ? "Red" : "Black") << "\n";
    f << "moveCount " << moveCount_ << "\n";
    f << "history " << moveHistory_.size() << "\n";
    f << "gameOver " << (gameOver_ ? 1 : 0) << "\n";
}

void GameScene::SetVerifyFlow(bool on) {
    s_verifyFlow = on;
}

// ═══════════════ --verify: scripted two-player flow ═══════════════
// Exercises the interactive paths that input injection cannot reach:
// mode switch, capture-to-tray + focus zoom, check flash/ring, undo
// (including tray revival), rematch, menu. Stage dumps land in
// verify_<stage>.txt next to the exe project root.
//
// Script (all moves verified legal from the opening position):
//   炮二平五 (19→22)  · 象3进5 (83→76)  — elephant screens file 4
//   炮五进四 (22→58)  — captures the center pawn THROUGH the red pawn
//                       screen → black is in check (elephant at 76)
//   象5退3 (76→83)   — answers the check (zero screens left → no check)
void GameScene::RunVerifyFlow() {
    ChooseMode(Mode::TwoPlayers);
    VerifyDump("01_start");

    auto moveAt = [this](float delay, int from, int to, const char* stage, bool shot) {
        scheduleOnce([this, from, to, stage, shot](float) {
            ExecuteMove({ from, to });
            // land at +0.45s (anim) → post-move state; mid-flash & mid-focus here
            scheduleOnce([this, stage, shot](float) {
                VerifyDump(stage);
                if (shot)
                    utils::captureScreen([](bool ok, const std::string& path) {
                        CCLOG("verifyShot ok=%d path=%s", ok ? 1 : 0, path.c_str());
                    }, std::string("D:/godogen/CocosChess/verify_") + stage + ".png");
            }, 1.3f, std::string("dump-") + stage);
        }, delay, std::string("move-") + stage);
    };

    moveAt(1.0f, 19, 22, "02_cannon_center", false);
    moveAt(3.0f, 83, 76, "03_elephant_screen", false);
    moveAt(5.0f, 22, 58, "04_capture_check", true); // shot: focus zoom + "将军！" flash
    moveAt(8.0f, 76, 83, "06_check_answered", true); // shot: wide view — tray holds the pawn

    auto undoAt = [this](float delay, const char* stage) {
        scheduleOnce([this, stage](float) { UndoGame(); VerifyDump(stage); }, delay,
                     std::string("undo-") + stage);
    };
    undoAt(10.0f, "07_undo1");          // pops the check answer → turn Black
    scheduleOnce([this](float) {        // pops the capture: pawn revives from the tray
        UndoGame();
        VerifyDump("08_undo2");
        utils::captureScreen([](bool, const std::string&) {},
            "D:/godogen/CocosChess/verify_08_undo2.png");
    }, 11.5f, "undo2-shot");
    undoAt(13.0f, "09a_undo3");
    undoAt(14.5f, "09b_undo_all");      // back to the opening position

    scheduleOnce([this](float) { RematchPressed(); VerifyDump("10_rematch"); },
                 16.0f, "rematch");
    scheduleOnce([this](float) {
        MenuPressed();
        VerifyDump("11_menu");
        utils::captureScreen([](bool, const std::string&) {},
            "D:/godogen/CocosChess/verify_11_menu.png");
    }, 18.0f, "menu");
}

void GameScene::VerifyDump(const char* stage) {
    std::string path = StringUtils::format("D:/godogen/CocosChess/verify_%s.txt", stage);
    std::ofstream f(path);
    if (!f) return;
    for (int i = 0; i < 90; i++) f << position_.cells[i] << (i == 89 ? '\n' : ' ');
    f << "turn " << (position_.turn == Side::Red ? "Red" : "Black") << "\n";
    f << "moveCount " << moveCount_ << "\n";
    f << "history " << moveHistory_.size() << "\n";
    int alive = 0;
    for (auto& pr : allPieces_) if (pr.alive) alive++;
    f << "alive " << alive << "\n";
    f << "redCaptured " << redCaptured_ << " blackCaptured " << blackCaptured_ << "\n";
    f << "inCheck " << (position_.InCheck(position_.turn) ? 1 : 0) << "\n";
    f << "gameStarted " << (gameStarted_ ? 1 : 0) << " gameOver " << (gameOver_ ? 1 : 0) << "\n";
    f << "animating " << (animating_ ? 1 : 0) << " aiThinking " << (aiThinking_ ? 1 : 0) << "\n";
}

// ═══════════════ audio ═══════════════

void GameScene::PlaySound(const char* name) {
    std::string path = StringUtils::format("audio/%s.wav", name);
    if (!FileUtils::getInstance()->isFileExist(path)) return; // Godot: missing files skip silently
    AudioEngine::play2d(path);
}

void GameScene::StartLoop(const char* name) {
    std::string path = StringUtils::format("audio/%s.wav", name);
    if (!FileUtils::getInstance()->isFileExist(path)) return;
    aiLoopId_ = AudioEngine::play2d(path, true);
}

void GameScene::StopLoop() {
    if (aiLoopId_ >= 0) {
        AudioEngine::stop(aiLoopId_);
        aiLoopId_ = -1;
    }
}

// ═══════════════ lifecycle ═══════════════

void GameScene::onEnter() {
    Scene::onEnter();
    touchListener_ = EventListenerTouchAllAtOnce::create();
    touchListener_->onTouchesBegan = CC_CALLBACK_2(GameScene::onTouchesBegan, this);
    touchListener_->onTouchesMoved = CC_CALLBACK_2(GameScene::onTouchesMoved, this);
    touchListener_->onTouchesEnded = CC_CALLBACK_2(GameScene::onTouchesEnded, this);
    _eventDispatcher->addEventListenerWithSceneGraphPriority(touchListener_, this);

    // desktop mouse hover → faint board hint (Board.ShowHover; buttonless moves only)
    mouseListener_ = EventListenerMouse::create();
    mouseListener_->onMouseMove = [this](Event* ev) {
        auto* m = dynamic_cast<EventMouse*>(ev);
        if (!hoverMark_) return;
        // desktop move with no button held leaves the default BUTTON_UNSET
        if (!m || m->getMouseButton() != EventMouse::MouseButton::BUTTON_UNSET ||
            !gameStarted_ || animating_) {
            hoverMark_->setVisible(false);
            return;
        }
        int idx = PickBoardIndex(m->getLocation());
        if (idx < 0) { hoverMark_->setVisible(false); return; }
        Vec3 w = BoardToWorld(idx);
        hoverMark_->setPosition3D(Vec3(w.x, 0.02f, w.z));
        hoverMark_->setVisible(true);
    };
    _eventDispatcher->addEventListenerWithSceneGraphPriority(mouseListener_, this);

    // keyboard: S screenshot / U undo / R rematch; I + digits edits the host IP
    // (desktop text entry without the ui::TextField IME stack)
    keyListener_ = EventListenerKeyboard::create();
    keyListener_->onKeyPressed = [this](EventKeyboard::KeyCode code, Event*) {
        if (code == EventKeyboard::KeyCode::KEY_S) {
            utils::captureScreen([](bool ok, const std::string& path) {
                CCLOG("captureLive ok=%d path=%s", ok ? 1 : 0, path.c_str());
            }, "D:/godogen/CocosChess/capture_" + s_bootTag + "_live.png");
            return;
        }
        if (netPanel_ && netPanel_->isVisible()) {
            if (ipEditing_) {
                int ci = (int)code;
                if (ci >= (int)EventKeyboard::KeyCode::KEY_0 &&
                    ci <= (int)EventKeyboard::KeyCode::KEY_9) {
                    ipBuffer_ += (char)('0' + ci - (int)EventKeyboard::KeyCode::KEY_0);
                } else if (code == EventKeyboard::KeyCode::KEY_PERIOD) {
                    ipBuffer_ += '.';
                } else if (code == EventKeyboard::KeyCode::KEY_BACKSPACE) {
                    if (!ipBuffer_.empty()) ipBuffer_.pop_back();
                } else if (code == EventKeyboard::KeyCode::KEY_ENTER ||
                           code == EventKeyboard::KeyCode::KEY_ESCAPE) {
                    ipEditing_ = false;
                } else {
                    return;
                }
                RefreshIpLabel();
                return;
            }
            if (code == EventKeyboard::KeyCode::KEY_I) {
                ipEditing_ = true;
                RefreshIpLabel();
                return;
            }
        }
        if (code == EventKeyboard::KeyCode::KEY_U) UndoGame();
        else if (code == EventKeyboard::KeyCode::KEY_R && gameOver_) RematchPressed();
    };
    _eventDispatcher->addEventListenerWithSceneGraphPriority(keyListener_, this);
}

void GameScene::onExit() {
    if (touchListener_) {
        _eventDispatcher->removeEventListener(touchListener_);
        touchListener_ = nullptr;
    }
    if (mouseListener_) {
        _eventDispatcher->removeEventListener(mouseListener_);
        mouseListener_ = nullptr;
    }
    if (keyListener_) {
        _eventDispatcher->removeEventListener(keyListener_);
        keyListener_ = nullptr;
    }
    net_.close(); // join the socket thread before the scene goes away
    StopLoop();
    for (auto& kv : modelCache_) // meshes hold their own refs; drop the cache's
        if (kv.second.tex) kv.second.tex->release();
    modelCache_.clear();
    Scene::onExit();
}
