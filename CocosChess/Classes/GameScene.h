// GameScene.h — 3D Xiangqi synced with the Godot 3DChess visual parameters
#pragma once
#include "cocos2d.h"
#include "ChessRules.h"
#include "AIPlayer.h"
#include "NetPlayer.h"
#include <future>
#include <map>
#include <string>
#include <vector>

class GameScene : public cocos2d::Scene {
public:
    static GameScene* create();

    // LAN-test automation: main() parses --net-host / --net-join and routes
    // the flags here so the two-instance sync check can run without input
    // injection (this session cannot synthesize touches).
    static void SetNetTestFlags(bool autoHost, bool autoJoin, const char* tag);
    // --verify: scripted two-player flow (mode switch / capture / check / undo
    // / rematch / menu) — same reason: drives interactive paths headlessly.
    static void SetVerifyFlow(bool on);

    virtual bool init() override;
    virtual void onEnter() override;
    virtual void onExit() override;
    virtual void update(float dt) override;

private:
    // ---- modes (Game.cs: VsAI / TwoPlayers / OnlineHost / OnlineGuest) ----
    enum class Mode { VsAI, TwoPlayers, OnlineHost, OnlineGuest };
    bool IsLocalTurn() const;
    chess::Side LocalSide() const;

    // ---- layout (Godot Board.S = 1.0, same grid) ----
    static constexpr float S = 1.0f;
    cocos2d::Vec3 BoardToWorld(int idx) const;
    int PickBoardIndex(const cocos2d::Vec2& screenPos) const;
    cocos2d::Vec3 TraySlot(int capturedValue);

    // ---- mesh generation ----
    static cocos2d::Mesh* CreateCylinder(float bottomR, float topR, float height, int seg);
    static cocos2d::Mesh* CreateDome(float radius, float squashY, int seg, int rings);
    static cocos2d::Mesh* CreateBox(float w, float h, float d);
    cocos2d::Sprite3D* MakeBoxNode(float w, float h, float d, const cocos2d::Color3B& color);

    // ---- rendering ----
    void BuildCamera();
    void BuildBoard();        // desk + slab + frame + plank stripes
    void BuildBoardLines();   // grid lines / palace diagonals / star marks as 3D boxes
    void BuildRiverText();
    void BuildTrays();
    void BuildMarkers();      // selection ring, check ring, last-move brackets, illegal flash, hover
    cocos2d::Sprite3D* CreatePiece3D(int cellValue, float* baseYawOut = nullptr);
    cocos2d::Sprite3D* LoadXModel(const std::string& path); // real-model loader (tools/glb_to_xmodel.py)
    // decoded .xmodel cache: 32 pieces share 14 files — each JPEG decodes once
    struct XModelData {
        std::vector<float> pos, nrm, uv;
        cocos2d::Mesh::IndexArray idx;
        cocos2d::Texture2D* tex = nullptr; // retained by the cache
    };
    std::map<std::string, XModelData> modelCache_;
    void RefreshAllPieces();
    void ShowMoveHints(const std::vector<chess::Move>& moves, int fromIdx);
    void ClearMoveHints();
    void ShowLastMove(const chess::Move& m);
    void HideLastMove();
    void ShowCheckRing(int kingIdx);   // -1 hides
    void ShowCheckFlash();             // HUD "将军！" text + shake (HUD.FlashCheck)
    void ShowIllegal(int idx);
    void ShowEnd(chess::Side winner, bool mate);   // end overlay with buttons
    void UpdateStatusLabel();

    // ---- UI overlays (HUD.cs) ----
    void BuildStartOverlay();   // mode selection: vs AI / two players / online
    void BuildNetworkPanel();   // host / join / IP / status
    void BuildEndOverlay();    // victory title + reason + rematch / menu
    void BuildGameButtons();    // in-game: 悔棋 | 菜单 | 再来一局
    void UpdateNetPanelStatus(const std::string& text);
    void RefreshIpLabel();

    // ---- interaction ----
    void onTap(const cocos2d::Vec2& screenPos);
    void TrySelect(int idx);
    void Deselect();
    void ExecuteMove(const chess::Move& m);
    void StartAITurn();

    // ---- match lifecycle (Game.cs ChooseMode/Rematch/Menu/Undo) ----
    void ChooseMode(Mode m);
    void ResetMatch();
    void MenuPressed();
    void RematchPressed();
    void UndoGame();
    void HandleOpponentLeft();
    void DumpNetState();        // --net-* diagnostics: board + turn to a file
    void RunVerifyFlow();       // --verify: scripted match, stage dumps + shots
    void VerifyDump(const char* stage);

    // ---- animation ----
    struct PieceAnim {
        cocos2d::Node* node;
        cocos2d::Vec3 from, to;
        float t;          // 0..1 over 0.45s (Godot Piece.AnimateMove)
        float arcHeight;  // 0.55 move / 0.9 capture-jump / 2.2 to-tray
        bool toTray;      // captured piece flying to the tray: shrinks to 0.82 at the end
    };
    struct Debris {
        cocos2d::Vec3 vel;
        float ttl;
    };
    struct HistEntry { chess::Move m; int captured; };
    // 32-piece registry: undo revives tray victims, ResetMatch walks all nodes
    struct PieceRef {
        cocos2d::Sprite3D* node;
        chess::Side side;
        chess::PieceType type;
        int idx;       // current board index (== m.to while dying in the tray)
        int startIdx;  // initial board index for ResetMatch
        bool alive;
        float baseYaw; // rest orientation: GLB red faces its player, procedural is inverted
    };
    PieceRef* RefAt(int idx); // alive-piece lookup by board index
    void SpawnDebris(const cocos2d::Vec3& pos, const cocos2d::Color3B& color, int count, float vmin, float vmax);
    std::vector<Debris> activeDebrisState_;
    void UpdateAnimations(float dt);
    void UpdateSelectedHover(float dt);
    void UpdateCamera(float dt);
    void FinishMove();       // mover landed: check ring, game end, AI turn

    // ---- audio ----
    void PlaySound(const char* name);
    void StartLoop(const char* name);
    void StopLoop();

    // ---- touch handlers (tap = pick; drag = orbit; 2-finger = pinch zoom) ----
    void onTouchesBegan(const std::vector<cocos2d::Touch*>& touches, cocos2d::Event* ev);
    void onTouchesMoved(const std::vector<cocos2d::Touch*>& touches, cocos2d::Event* ev);
    void onTouchesEnded(const std::vector<cocos2d::Touch*>& touches, cocos2d::Event* ev);

    // ---- state ----
    chess::Position position_;
    Mode mode_ = Mode::VsAI;
    bool gameStarted_ = false;
    int selectedIdx_ = -1;
    bool aiThinking_ = false;
    bool gameOver_ = false;
    bool animating_ = false;          // mover animation in flight; input gated
    int moveCount_ = 0;
    std::vector<chess::Move> legalFromSelected_;
    std::vector<HistEntry> moveHistory_;   // (move, captured piece value) for UndoGame
    std::vector<PieceRef> allPieces_;      // all 32 piece nodes, tray victims included
    std::future<chess::Move> aiFuture_;
    chess::Move pendingAiMove_{ -1, -1 };
    bool aiMovePending_ = false;
    int aiGen_ = 0;                   // bumped by ResetMatch; stale AI results are dropped
    int aiGenAtStart_ = 0;
    float aiApplyDelay_ = 0.0f;
    float endSoundDelay_ = 0.0f;
    int pendingEndSound_ = 0;        // 1 = victory, 2 = defeat (played after endSoundDelay_)
    float pulseT_ = 0.0f;
    float bobT_ = 0.0f;              // selected-piece hover timer
    float hoverSpin_ = 0.0f;         // selected-piece spin angle (rad)
    int redCaptured_ = 0, blackCaptured_ = 0;

    // ---- 3D nodes ----
    float camYaw_ = 0.0f, camPitch_ = 0.88f, camDist_ = 14.2f;   // Godot Game.cs orbit state
    cocos2d::Vec3 camPos_{ 0.0f, 10.95f, 9.65f };
    cocos2d::Vec3 focusPos_{ 0.0f, 0.0f, 0.3f };  // cinematic capture zoom (Game.cs _focusTimer)
    float focusTimer_ = 0.0f;
    static constexpr float FOCUS_HOLD = 1.2f;
    static constexpr float FOCUS_DIST = 6.0f;
    cocos2d::Camera* cam3d_ = nullptr;
    cocos2d::Node* piecesLayer_ = nullptr;
    cocos2d::Node* trayLayer_ = nullptr;     // captured pieces fly here
    std::vector<cocos2d::Sprite3D*> hintDots_;   // pooled green dots (init-created)
    std::vector<cocos2d::Sprite3D*> hintRings_;  // pooled capture rings (init-created)
    std::vector<cocos2d::Sprite3D*> debrisPool_; // pooled debris cubes (init-created)
    std::vector<cocos2d::Sprite3D*> activeDebris_; // pool slots currently in flight
    cocos2d::Sprite3D* selectedRing_ = nullptr;
    cocos2d::Sprite3D* checkRing_ = nullptr;
    cocos2d::Sprite3D* hoverMark_ = nullptr;   // desktop mouse hint (Board.ShowHover)
    std::vector<cocos2d::Sprite3D*> lastFromBoxes_;  // 8 boxes per bracket, direct scene children
    std::vector<cocos2d::Sprite3D*> lastToBoxes_;
    cocos2d::Sprite3D* illegalMark_ = nullptr;
    float illegalT_ = 0.0f;

    // ---- 2D UI ----
    cocos2d::Label* statusLabel_ = nullptr;
    cocos2d::Label* fpsLabel_ = nullptr;
    cocos2d::Label* checkFlashLabel_ = nullptr;  // "将军！" flash
    float checkFlashT_ = 0.0f, checkShakeT_ = 0.0f;
    cocos2d::Vec2 checkFlashBase_{ 0.0f, 0.0f };
    cocos2d::LayerColor* startOverlay_ = nullptr;
    cocos2d::LayerColor* netPanel_ = nullptr;
    cocos2d::LayerColor* endOverlay_ = nullptr;
    cocos2d::Label* endTitleLabel_ = nullptr;
    cocos2d::Label* endReasonLabel_ = nullptr;
    cocos2d::Label* netStatusLabel_ = nullptr;
    cocos2d::Label* ipLabel_ = nullptr;   // keyboard-driven IP entry display
    cocos2d::Menu* gameButtons_ = nullptr;
    std::string ipBuffer_ = "127.0.0.1";
    bool ipEditing_ = false;
    int fpsFrames_ = 0;
    float fpsAccum_ = 0.0f;

    // ---- network ----
    NetPlayer net_;

    // ---- listeners ----
    cocos2d::EventListenerTouchAllAtOnce* touchListener_ = nullptr;
    cocos2d::EventListenerMouse* mouseListener_ = nullptr;
    cocos2d::EventListenerKeyboard* keyListener_ = nullptr;
    std::vector<PieceAnim> activeAnims_;

    // ---- touch state ----
    cocos2d::Vec2 touchStart_ = cocos2d::Vec2::ZERO;
    bool touchDragging_ = false;
    float lastPinchDist_ = 0.0f;

    int aiLoopId_ = -1;
};
