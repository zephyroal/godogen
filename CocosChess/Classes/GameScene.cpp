// GameScene.cpp — 3D Xiangqi on Cocos2d-x 4, synced with Godot 3DChess visual parameters
#include "GameScene.h"
#include <cmath>

USING_NS_CC;
using namespace chess;

// ═══════════════ Godot parameter constants ═══════════════

// Camera (Game.cs L37-46, L213-240)
static constexpr float CAM_FOV    = 38.0f;
static constexpr float CAM_PITCH  = 0.88f;
static constexpr float CAM_DIST   = 14.2f;
static const Vec3 CAM_TARGET = Vec3(0.0f, 0.0f, 0.3f);

// Board colors (Board.cs — Mat() / WoodGrain() params)
static const Color3B DESK_COLOR   = Color3B(72, 46, 26);   // dark wood (0.34,0.22,0.13)*~212 avg
static const Color3B SLAB_COLOR  = Color3B(217, 173, 115); // light wood (0.85,0.68,0.45)
static const Color3B LINE_COLOR  = Color3B(77, 48, 26);    // (0.30,0.19,0.10)
static const Color3B STAR_COLOR  = Color3B(82, 51, 26);     // (0.32,0.20,0.10)
static const Color3B RIVER_COLOR = Color3B(82, 51, 26);

// Piece colors (Piece.cs — wood/rim materials)
static const Color3B WOOD_RED    = Color3B(224, 179, 120); // (0.88,0.70,0.47)
static const Color3B WOOD_BLACK  = Color3B(189, 148, 97);  // (0.74,0.58,0.38)
static const Color3B RIM_RED    = Color3B(158, 41, 31);   // (0.62,0.16,0.12)
static const Color3B RIM_BLACK  = Color3B(41, 38, 36);     // (0.16,0.15,0.14)
static const Color4B GLYPH_RED   = Color4B(173, 31, 20, 255);  // (0.68,0.12,0.08)
static const Color4B GLYPH_BLACK = Color4B(41, 36, 31, 255);   // (0.16,0.14,0.12)
static const Color4B GLYPH_OUTLINE = Color4B(242, 230, 199, 255); // (0.95,0.9,0.78)

// Highlight colors (Board.cs markers)
static const Color3B HINT_DOT    = Color3B(102, 230, 102);  // (0.4,0.9,0.4)
static const Color3B HINT_RING   = Color3B(255, 89, 64);    // (1,0.35,0.25)
static const Color3B SEL_RING    = Color3B(255, 217, 77);   // (1,0.85,0.3)
static const Color3B CHECK_RING  = Color3B(255, 51, 38);    // (1,0.2,0.15)

// Background (Environment sky approximation — warm brown)
static const Color4B BG_COLOR = Color4B(56, 41, 28, 255);  // (0.22,0.16,0.11)

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
        idx.insert(idx.end(), { (unsigned short)(i*2), (unsigned short)((i+1)*2), (unsigned short)(i*2+1) });
        idx.insert(idx.end(), { (unsigned short)(i*2+1), (unsigned short)((i+1)*2), (unsigned short)((i+1)*2+1) });
    }
    int tc = (int)(pos.size() / 3);
    pos.insert(pos.end(), { 0, height, 0 }); nrm.insert(nrm.end(), { 0, 1, 0 }); uv.insert(uv.end(), { .5f, .5f });
    for (int i = 0; i < seg; i++)
        idx.insert(idx.end(), { (unsigned short)tc, (unsigned short)((i+1)*2+1), (unsigned short)(i*2+1) });
    int bc = (int)(pos.size() / 3);
    pos.insert(pos.end(), { 0, 0, 0 }); nrm.insert(nrm.end(), { 0, -1, 0 }); uv.insert(uv.end(), { .5f, .5f });
    for (int i = 0; i < seg; i++)
        idx.insert(idx.end(), { (unsigned short)bc, (unsigned short)(i*2), (unsigned short)((i+1)*2) });
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
            float len = std::sqrt(rad*rad + y*y);
            nrm.insert(nrm.end(), { rad*c/len, y/len, rad*s/len });
            uv.insert(uv.end(), { (float)i/seg, (float)r/rings });
        }
    }
    // quads between rings
    for (int r = 0; r < rings; r++) {
        for (int i = 0; i < seg; i++) {
            int v0 = r * (seg+1) + i;
            int v1 = r * (seg+1) + i+1;
            int v2 = (r+1) * (seg+1) + i;
            int v3 = (r+1) * (seg+1) + i+1;
            idx.insert(idx.end(), { (unsigned short)v0, (unsigned short)v1, (unsigned short)v2 });
            idx.insert(idx.end(), { (unsigned short)v1, (unsigned short)v3, (unsigned short)v2 });
        }
    }
    // bottom disc
    int bc = (int)(pos.size() / 3);
    pos.insert(pos.end(), { 0, 0, 0 }); nrm.insert(nrm.end(), { 0, -1, 0 }); uv.insert(uv.end(), { .5f, .5f });
    for (int i = 0; i < seg; i++) {
        int v0 = 0 * (seg+1) + i;
        int v1 = 0 * (seg+1) + i+1;
        idx.insert(idx.end(), { (unsigned short)bc, (unsigned short)v0, (unsigned short)v1 });
    }
    return Mesh::create(pos, nrm, uv, idx);
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

    auto visible = Director::getInstance()->getVisibleSize();
    unsigned short cam3dMask = (unsigned short)CameraFlag::USER1;

    // background (approximating Godot's dark ground color)
    auto bg = LayerColor::create(BG_COLOR, visible.width, visible.height);
    bg->setCameraMask(2);
    addChild(bg, -10);

    BuildCamera();
    BuildBoard();
    BuildGridOverlay();

    piecesLayer_ = Node::create();
    piecesLayer_->setCameraMask(cam3dMask);
    addChild(piecesLayer_);

    hintsLayer_ = Node::create();
    hintsLayer_->setCameraMask(cam3dMask);
    addChild(hintsLayer_);

    // selection ring (Godot: torus 0.34-0.44, gold, pulsing 1+0.08sin(4t))
    selectedRing_ = Sprite3D::create();
    selectedRing_->addMesh(CreateCylinder(0.44f, 0.44f, 0.03f, 40));
    selectedRing_->genMaterial(false);
    selectedRing_->setColor(SEL_RING);
    selectedRing_->setCameraMask(cam3dMask);
    selectedRing_->setCullFaceEnabled(false);
    selectedRing_->setVisible(false);
    addChild(selectedRing_);
    pulseMarkers_.push_back(selectedRing_);

    // 2D status bar
    statusLabel_ = Label::createWithSystemFont("红方走棋 — 点击棋子选择", "Microsoft YaHei", 26);
    if (statusLabel_) {
        statusLabel_->setTextColor(Color4B(240, 230, 210, 255));
        statusLabel_->setPosition(Vec2(visible.width / 2, visible.height - 34));
        statusLabel_->setCameraMask(2);
        addChild(statusLabel_, 10);
    }

    RefreshAllPieces();
    return true;
}

void GameScene::BuildCamera() {
    auto visible = Director::getInstance()->getVisibleSize();
    cam3d_ = Camera::createPerspective(CAM_FOV, visible.width / visible.height, 0.1f, 100.0f);
    // Godot: off = (sin(0)*cos(0.88), sin(0.88), cos(0)*cos(0.88)) * 14.2 + target(0,0,0.3)
    float offY = std::sin(CAM_PITCH) * CAM_DIST;   // = 10.95
    float offZ = std::cos(CAM_PITCH) * CAM_DIST;   // = 9.05
    cam3d_->setPosition3D(Vec3(0.0f, offY, CAM_TARGET.z + offZ));
    cam3d_->lookAt(CAM_TARGET, Vec3(0.0f, 1.0f, 0.0f));
    cam3d_->setCameraFlag(CameraFlag::USER1);
    addChild(cam3d_);
}

void GameScene::BuildBoard() {
    unsigned short mask = (unsigned short)CameraFlag::USER1;

    // desk (Godot: BoxMesh 11.5×0.8×12.5 @ y=-0.55, dark wood)
    deskNode_ = Sprite3D::create();
    {
        float hw = 5.75f, hh = 0.4f, hd = 6.25f;
        std::vector<float> pos = { -hw,hh,-hd, hw,hh,-hd, hw,hh,hd, -hw,hh,hd,
                                   -hw,-hh,-hd, hw,-hh,-hd, hw,-hh,hd, -hw,-hh,hd };
        std::vector<float> nrm; for (int i = 0; i < 8; i++) nrm.insert(nrm.end(), { 0, (i<4)?1.f:-1.f, 0 });
        std::vector<float> uv = { 0,0, 1,0, 1,1, 0,1, 0,0, 1,0, 1,1, 0,1 };
        Mesh::IndexArray idx = { 0,1,2, 0,2,3, 4,6,5, 4,7,6, 0,4,5, 0,5,1, 3,2,6, 3,6,7, 0,3,7, 0,7,4, 1,5,6, 1,6,2 };
        deskNode_->addMesh(Mesh::create(pos, nrm, uv, idx));
    }
    deskNode_->genMaterial(false);
    deskNode_->setColor(DESK_COLOR);
    deskNode_->setCameraMask(mask);
    deskNode_->setCullFaceEnabled(false);
    deskNode_->setPosition3D(Vec3(0.0f, -0.55f, 0.0f));
    addChild(deskNode_);

    // slab (Godot: BoxMesh 9.6×0.32×10.6 @ y=-0.16, light wood)
    slabNode_ = Sprite3D::create();
    {
        float hw = 4.8f, hh = 0.16f, hd = 5.3f;
        std::vector<float> pos = { -hw,hh,-hd, hw,hh,-hd, hw,hh,hd, -hw,hh,hd,
                                   -hw,-hh,-hd, hw,-hh,-hd, hw,-hh,hd, -hw,-hh,hd };
        std::vector<float> nrm; for (int i = 0; i < 8; i++) nrm.insert(nrm.end(), { 0, (i<4)?1.f:-1.f, 0 });
        std::vector<float> uv = { 0,0, 1,0, 1,1, 0,1, 0,0, 1,0, 1,1, 0,1 };
        Mesh::IndexArray idx = { 0,1,2, 0,2,3, 4,6,5, 4,7,6, 0,4,5, 0,5,1, 3,2,6, 3,6,7, 0,3,7, 0,7,4, 1,5,6, 1,6,2 };
        slabNode_->addMesh(Mesh::create(pos, nrm, uv, idx));
    }
    slabNode_->genMaterial(false);
    slabNode_->setColor(SLAB_COLOR);
    slabNode_->setCameraMask(mask);
    slabNode_->setCullFaceEnabled(false);
    slabNode_->setPosition3D(Vec3(0.0f, -0.16f, 0.0f));
    addChild(slabNode_);
}

void GameScene::BuildGridOverlay() {
    unsigned short mask = (unsigned short)CameraFlag::USER1;
    gridOverlay_ = Node::create();
    gridOverlay_->setCameraMask(mask);
    gridOverlay_->setRotation3D(Vec3(-90.0f, 0.0f, 0.0f));
    gridOverlay_->setPosition3D(Vec3(0.0f, 0.006f, 0.0f)); // Godot: Y=0.006
    addChild(gridOverlay_);

    auto dn = DrawNode::create();
    Color4F line((float)LINE_COLOR.r/255, (float)LINE_COLOR.g/255, (float)LINE_COLOR.b/255, 1.0f);

    // local x = world x, local y = world z; red (rank 0) at local y = +4.5*S
    auto P = [](int f, int r) { return Vec2((f - 4) * S, (4.5f - r) * S); };

    // 10 horizontal lines
    for (int r = 0; r < 10; r++)
        dn->drawSegment(P(0, r), P(8, r), 0.045f * 15, line); // 0.045 world ≈ 0.045*15 px
    // 9 vertical lines
    for (int f = 0; f < 9; f++) {
        if (f == 0 || f == 8)
            dn->drawSegment(P(f, 0), P(f, 9), 0.045f * 15, line);
        else {
            dn->drawSegment(P(f, 0), P(f, 4), 0.045f * 15, line);
            dn->drawSegment(P(f, 5), P(f, 9), 0.045f * 15, line);
        }
    }
    // palace diagonals
    for (int zc : { 1, 8 }) {
        dn->drawSegment(P(3, zc-1), P(5, zc+1), 0.045f * 15, line);
        dn->drawSegment(P(5, zc-1), P(3, zc+1), 0.045f * 15, line);
    }
    // star marks (4 small squares per point)
    Color4F star((float)STAR_COLOR.r/255, (float)STAR_COLOR.g/255, (float)STAR_COLOR.b/255, 1.0f);
    auto markStar = [&](int f, int r) {
        float x = (f-4)*S, y = (4.5f-r)*S;
        for (int sx : { -1, 1 }) for (int sy : { -1, 1 }) {
            dn->drawSegment(Vec2(x + sx*0.13f*S, y + sy*0.13f*S),
                            Vec2(x + sx*0.23f*S, y + sy*0.13f*S), 1.5f, star);
            dn->drawSegment(Vec2(x + sx*0.13f*S, y + sy*0.13f*S),
                            Vec2(x + sx*0.13f*S, y + sy*0.23f*S), 1.5f, star);
        }
    };
    for (int r : { 2, 7 }) for (int f : { 1, 7 }) markStar(f, r);
    for (int r : { 3, 6 }) for (int f : { 0, 2, 4, 6, 8 }) markStar(f, r);

    gridOverlay_->addChild(dn);

    // river text (Godot: FontSize 88, at x=±2.5, z=0)
    auto font = "Microsoft YaHei";
    auto rl = Label::createWithSystemFont("楚 河", font, 52);
    if (rl) { rl->setTextColor(Color4B(RIVER_COLOR.r, RIVER_COLOR.g, RIVER_COLOR.b, 255));
              rl->setPosition(Vec2(-2.5f*S, 0)); gridOverlay_->addChild(rl); }
    auto rr = Label::createWithSystemFont("汉 界", font, 52);
    if (rr) { rr->setTextColor(Color4B(RIVER_COLOR.r, RIVER_COLOR.g, RIVER_COLOR.b, 255));
              rr->setPosition(Vec2(2.5f*S, 0)); gridOverlay_->addChild(rr); }
}

// ═══════════════ pieces ═══════════════

Sprite3D* GameScene::CreatePiece3D(int cellValue) {
    unsigned short mask = (unsigned short)CameraFlag::USER1;
    bool isRed = cellValue > 0;
    auto type = (PieceType)std::abs(cellValue);
    auto side = isRed ? Side::Red : Side::Black;
    Color3B wood = isRed ? WOOD_RED : WOOD_BLACK;
    Color3B rim  = isRed ? RIM_RED : RIM_BLACK;

    // Per-type body stretch factor (approximates the Godot GLB height differentiation)
    float bodyStretch;
    switch (type) {
        case PieceType::King:     bodyStretch = 2.0f; break;
        case PieceType::Advisor:  bodyStretch = 1.4f; break;
        case PieceType::Elephant: bodyStretch = 1.5f; break;
        case PieceType::Horse:    bodyStretch = 1.7f; break;
        case PieceType::Chariot:  bodyStretch = 1.7f; break;
        case PieceType::Cannon:  bodyStretch = 1.7f; break;
        default:                  bodyStretch = 1.0f; break; // soldier
    }

    // Godot procedural dimensions (Piece.cs BuildProcedural)
    // base: cyl(top=0.31,bot=0.43,h=0.10) @ y=0.05
    // body: cyl(top=0.34,bot=0.31,h=0.16) @ y=0.18 — stretched per type
    // rim:  torus(in=0.26,out=0.37) @ y=0.29 — approximated with thin cylinder
    // cap:  cyl(top=0.27,bot=0.34,h=0.07) @ y=0.335
    // dome: sphere(r=0.30,sy=0.42) @ y=0.37
    // glyph: Label3D @ y=0.53, rot(-90,0,0), PixelSize=0.004
    float bodyH = 0.16f * bodyStretch;
    float yBase = 0.10f;
    float yBody = yBase + bodyH;
    float yRim  = yBody + 0.02f;
    float yCap  = yRim + 0.02f;
    float yDome = yCap + 0.07f;
    float yGlyph = yDome + 0.30f * 0.42f + 0.05f;

    auto piece = Sprite3D::create();

    // 1. base disc
    auto base = Sprite3D::create();
    base->addMesh(CreateCylinder(0.43f, 0.31f, 0.10f, 32));
    base->genMaterial(false); base->setColor(wood);
    base->setCameraMask(mask); base->setCullFaceEnabled(false);
    base->setPosition3D(Vec3(0, 0.05f, 0));
    piece->addChild(base);

    // 2. body (tapered, stretched per type)
    auto body = Sprite3D::create();
    body->addMesh(CreateCylinder(0.31f, 0.34f, bodyH, 32));
    body->genMaterial(false); body->setColor(wood);
    body->setCameraMask(mask); body->setCullFaceEnabled(false);
    body->setPosition3D(Vec3(0, yBase + bodyH * 0.5f, 0));
    piece->addChild(body);

    // 3. rim ring (approximate torus with thin wide cylinder)
    auto ring = Sprite3D::create();
    ring->addMesh(CreateCylinder(0.37f, 0.37f, 0.035f, 32));
    ring->genMaterial(false); ring->setColor(rim);
    ring->setCameraMask(mask); ring->setCullFaceEnabled(false);
    ring->setPosition3D(Vec3(0, yRim, 0));
    piece->addChild(ring);

    // 4. cap (tapered down)
    auto cap = Sprite3D::create();
    cap->addMesh(CreateCylinder(0.34f, 0.27f, 0.07f, 32));
    cap->genMaterial(false); cap->setColor(wood);
    cap->setCameraMask(mask); cap->setCullFaceEnabled(false);
    cap->setPosition3D(Vec3(0, yCap, 0));
    piece->addChild(cap);

    // 5. dome (squashed hemisphere)
    auto dome = Sprite3D::create();
    dome->addMesh(CreateDome(0.30f, 0.42f, 24, 8));
    dome->genMaterial(false); dome->setColor(wood);
    dome->setCameraMask(mask); dome->setCullFaceEnabled(false);
    dome->setPosition3D(Vec3(0, yDome, 0));
    piece->addChild(dome);

    // 6. Chinese glyph — flat on top, facing UP (matching Godot Label3D rot -90°X)
    auto label = Label::createWithSystemFont(PieceChar(type, side), "Microsoft YaHei", 44);
    if (label) {
        label->setTextColor(isRed ? GLYPH_RED : GLYPH_BLACK);
        label->enableOutline(GLYPH_OUTLINE, 3);
        label->setCameraMask(mask);
        label->setRotation3D(Vec3(-90.0f, 0.0f, 0.0f)); // lie flat, face up
        label->setPosition3D(Vec3(0.0f, yGlyph, 0.0f));
        label->setScale(0.004f); // matching Godot PixelSize=0.004
        label->setGlobalZOrder(10);
        piece->addChild(label);
    }

    piece->setCameraMask(mask);
    return piece;
}

void GameScene::SyncPieceNode(int idx) {
    auto node = piecesLayer_->getChildByTag(idx);
    int cell = position_.cells[idx];
    if (cell == 0) { if (node) node->removeFromParent(); return; }
    if (!node) {
        node = CreatePiece3D(cell);
        node->setTag(idx);
        node->setPosition3D(BoardToWorld(idx));
        piecesLayer_->addChild(node);
    } else {
        // arc animation (Godot: 0.45s total, arcHeight 0.55 normal / 0.9 capture)
        Vec3 from = node->getPosition3D();
        Vec3 to = BoardToWorld(idx);
        node->stopAllActions();
        // MoveTo for XZ + custom arc via Sequence
        float dur = 0.45f; // matching Godot
        float arcH = 0.55f; // normal move
        // Use a simple two-action approach: horizontal MoveTo + vertical sine
        auto moveTo = MoveTo::create(dur, to);
        // vertical arc: piece rises then falls
        auto up = MoveTo::create(dur * 0.5f, Vec3((from.x+to.x)/2, arcH, (from.z+to.z)/2));
        node->runAction(Sequence::create(up, moveTo, nullptr));
    }
}

void GameScene::RefreshAllPieces() {
    for (int i = 0; i < 90; i++) SyncPieceNode(i);
}

void GameScene::ShowMoveHints(const std::vector<Move>& moves, int fromIdx) {
    ClearMoveHints();
    unsigned short mask = (unsigned short)CameraFlag::USER1;
    for (auto& m : moves) {
        if (m.from != fromIdx) continue;
        Vec3 w = BoardToWorld(m.to);
        bool capture = position_.cells[m.to] != 0;
        auto marker = Sprite3D::create();
        if (capture) {
            // Godot: torus(0.3-0.4) red ring, Emission(1,0.3,0.2)×1.9
            marker->addMesh(CreateCylinder(0.40f, 0.40f, 0.025f, 36));
            marker->setColor(HINT_RING);
        } else {
            // Godot: cyl(R=0.16) green dot, Emission(0.3,0.9,0.3)×1.6, pulsing
            marker->addMesh(CreateCylinder(0.16f, 0.16f, 0.02f, 24));
            marker->setColor(HINT_DOT);
        }
        marker->genMaterial(false);
        marker->setCameraMask(mask);
        marker->setCullFaceEnabled(false);
        marker->setPosition3D(Vec3(w.x, 0.035f, w.z)); // Godot: Y=0.035
        hintsLayer_->addChild(marker);
    }
    // selection ring
    if (selectedRing_ && selectedIdx_ >= 0) {
        Vec3 w = BoardToWorld(selectedIdx_);
        selectedRing_->setPosition3D(Vec3(w.x, 0.03f, w.z));
        selectedRing_->setVisible(true);
    }
}

void GameScene::ClearMoveHints() {
    hintsLayer_->removeAllChildren();
    if (selectedRing_) selectedRing_->setVisible(false);
}

// ═══════════════ picking ═══════════════

Vec3 GameScene::BoardToWorld(int idx) const {
    int f = File(idx), r = Rank(idx);
    return Vec3((f - 4) * S, 0.0f, (4.5f - r) * S);
}

int GameScene::PickBoardIndex(const Vec2& screenPos) const {
    if (!cam3d_) return -1;
    Vec3 w0 = cam3d_->unproject(Vec3(screenPos.x, screenPos.y, 0.0f));
    Vec3 w1 = cam3d_->unproject(Vec3(screenPos.x, screenPos.y, 1.0f));
    Vec3 dir = w1 - w0;
    dir.normalize();
    if (std::fabs(dir.y) < 1e-6f) return -1;
    float t = -w0.y / dir.y;
    if (t < 0) return -1;
    Vec3 hit = w0 + dir * t;
    int f = (int)std::lround(hit.x / S + 4.0f);
    int r = (int)std::lround(4.5f - hit.z / S);
    if (f < 0 || f > 8 || r < 0 || r > 9) return -1;
    Vec3 exact = BoardToWorld(Idx(f, r));
    if (std::fabs(hit.x - exact.x) > S * 0.55f) return -1;
    if (std::fabs(hit.z - exact.z) > S * 0.55f) return -1;
    return Idx(f, r);
}

// ═══════════════ touch ═══════════════

bool GameScene::onTouchBegan(Touch*, Event*) {
    return !gameOver_ && !aiThinking_ && position_.turn == humanSide_;
}

void GameScene::onTouchEnded(Touch* touch, Event*) {
    if (gameOver_ || aiThinking_ || position_.turn != humanSide_) return;
    int idx = PickBoardIndex(touch->getLocation());
    if (idx < 0) { selectedIdx_ = -1; legalFromSelected_.clear(); ClearMoveHints(); return; }
    if (selectedIdx_ >= 0) {
        for (auto& m : legalFromSelected_) {
            if (m.to == idx) { ExecuteMove({ selectedIdx_, idx }); return; }
        }
    }
    TrySelect(idx);
}

void GameScene::TrySelect(int idx) {
    if (!position_.Own(idx, position_.turn)) {
        selectedIdx_ = -1; legalFromSelected_.clear(); ClearMoveHints();
        return;
    }
    selectedIdx_ = idx;
    auto all = position_.LegalMoves(position_.turn);
    legalFromSelected_.clear();
    for (auto& m : all)
        if (m.from == idx) legalFromSelected_.push_back(m);
    ShowMoveHints(legalFromSelected_, idx);
}

// ═══════════════ game flow ═══════════════

void GameScene::ExecuteMove(const Move& m) {
    int captured = 0;
    position_.MakeMove(m, captured);
    selectedIdx_ = -1;
    legalFromSelected_.clear();
    ClearMoveHints();
    SyncPieceNode(m.from);
    SyncPieceNode(m.to);
    if (!CheckGameEnd()) {
        if (position_.turn != humanSide_) StartAITurn();
        UpdateStatusLabel();
    }
}

void GameScene::StartAITurn() {
    aiThinking_ = true;
    UpdateStatusLabel();
    Position snapshot = position_;
    Side aiSide = position_.turn;
    aiFuture_ = std::async(std::launch::async, [snapshot, aiSide]() -> Move {
        AIPlayer ai(aiSide);
        int depth = 0; long long nodes = 0;
        return ai.Search(snapshot, 900, depth, nodes); // Godot: 900ms cap
    });
}

void GameScene::update(float dt) {
    if (aiThinking_ && aiFuture_.valid()) {
        if (aiFuture_.wait_for(std::chrono::seconds(0)) == std::future_status::ready) {
            Move m = aiFuture_.get();
            aiThinking_ = false;
            if (!gameOver_) ExecuteMove(m);
        }
    }
    // pulsing markers (Godot: sin(t*4~6) breathing)
    pulseT_ += dt;
    if (selectedRing_ && selectedRing_->isVisible()) {
        float s = 1.0f + 0.08f * std::sin(pulseT_ * 4.0f); // Godot: 1+0.08sin(4t)
        selectedRing_->setScale(s);
    }
    for (auto child : hintsLayer_->getChildren()) {
        float s = 1.0f + 0.12f * std::sin(pulseT_ * 5.0f); // Godot: 1+0.12sin(5t)
        child->setScale(s);
    }
}

bool GameScene::CheckGameEnd() {
    Side toMove = position_.turn;
    if (position_.IsCheckmate(toMove)) {
        gameOver_ = true;
        ShowGameOver(std::string("绝杀！") + (toMove == Side::Red ? "黑方胜" : "红方胜"));
        return true;
    }
    if (position_.IsStalemate(toMove)) {
        gameOver_ = true;
        ShowGameOver(std::string("困毙 — ") + (toMove == Side::Red ? "黑方胜" : "红方胜"));
        return true;
    }
    if (position_.InCheck(toMove) && statusLabel_) {
        statusLabel_->setString((toMove == Side::Red ? "红方" : "黑方") + std::string(" 被将军！"));
    }
    return false;
}

void GameScene::ShowGameOver(const std::string& text) {
    if (endLabel_) endLabel_->removeFromParent();
    endLabel_ = Label::createWithSystemFont(text, "Microsoft YaHei", 46);
    if (endLabel_) {
        auto visible = Director::getInstance()->getVisibleSize();
        endLabel_->setTextColor(Color4B(255, 220, 100, 255));
        endLabel_->enableOutline(Color4B(30, 20, 10, 220), 3);
        endLabel_->setPosition(Vec2(visible.width / 2, visible.height / 2));
        endLabel_->setCameraMask(2);
        addChild(endLabel_, 20);
    }
    UpdateStatusLabel();
}

void GameScene::UpdateStatusLabel() {
    if (!statusLabel_) return;
    if (gameOver_) { statusLabel_->setString("对局结束"); return; }
    if (aiThinking_) { statusLabel_->setString("AI 思考中…"); return; }
    statusLabel_->setString(position_.turn == Side::Red
        ? "红方走棋 — 点击棋子选择"
        : "黑方走棋");
}

// ═══════════════ lifecycle ═══════════════

void GameScene::onEnter() {
    Scene::onEnter();
    touchListener_ = EventListenerTouchOneByOne::create();
    touchListener_->onTouchBegan = CC_CALLBACK_2(GameScene::onTouchBegan, this);
    touchListener_->onTouchEnded = CC_CALLBACK_2(GameScene::onTouchEnded, this);
    touchListener_->setSwallowTouches(true);
    _eventDispatcher->addEventListenerWithSceneGraphPriority(touchListener_, this);
}

void GameScene::onExit() {
    if (touchListener_) {
        _eventDispatcher->removeEventListener(touchListener_);
        touchListener_ = nullptr;
    }
    Scene::onExit();
}
