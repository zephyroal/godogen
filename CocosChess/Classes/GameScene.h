// GameScene.h — 3D Xiangqi synced with the Godot version's exact visual parameters
#pragma once
#include "cocos2d.h"
#include "ChessRules.h"
#include "AIPlayer.h"
#include <future>

class GameScene : public cocos2d::Scene {
public:
    static GameScene* create();

    virtual bool init() override;
    virtual void onEnter() override;
    virtual void onExit() override;
    virtual void update(float dt) override;

private:
    // ---- layout (Godot Board.S = 1.0, same grid) ----
    static constexpr float S = 1.0f;
    cocos2d::Vec3 BoardToWorld(int idx) const;
    int PickBoardIndex(const cocos2d::Vec2& screenPos) const;

    // ---- mesh generation ----
    static cocos2d::Mesh* CreateCylinder(float bottomR, float topR, float height, int seg);
    static cocos2d::Mesh* CreateDome(float radius, float squashY, int seg, int rings);

    // ---- rendering ----
    void BuildCamera();
    void BuildBoard();
    void BuildGridOverlay();
    cocos2d::Sprite3D* CreatePiece3D(int cellValue);
    void SyncPieceNode(int idx);
    void RefreshAllPieces();
    void ShowMoveHints(const std::vector<chess::Move>& moves, int fromIdx);
    void ClearMoveHints();
    void ShowGameOver(const std::string& text);
    void UpdateStatusLabel();

    // ---- interaction ----
    bool onTouchBegan(cocos2d::Touch* touch, cocos2d::Event* ev);
    void onTouchEnded(cocos2d::Touch* touch, cocos2d::Event* ev);
    void TrySelect(int idx);
    void ExecuteMove(const chess::Move& m);

    // ---- animation ----
    void AnimatePieceMove(cocos2d::Node* piece, const cocos2d::Vec3& from, const cocos2d::Vec3& to, bool highArc);
    void UpdatePulse(float dt);

    // ---- game flow ----
    void StartAITurn();
    bool CheckGameEnd();

    // ---- state ----
    chess::Position position_;
    chess::Side humanSide_ = chess::Side::Red;
    int selectedIdx_ = -1;
    bool aiThinking_ = false;
    bool gameOver_ = false;
    std::vector<chess::Move> legalFromSelected_;
    std::future<chess::Move> aiFuture_;
    float pulseT_ = 0.0f;

    // ---- 3D nodes ----
    cocos2d::Camera* cam3d_ = nullptr;
    cocos2d::Sprite3D* deskNode_ = nullptr;
    cocos2d::Sprite3D* slabNode_ = nullptr;
    cocos2d::Node* gridOverlay_ = nullptr;
    cocos2d::Node* piecesLayer_ = nullptr;
    cocos2d::Node* hintsLayer_ = nullptr;
    cocos2d::Sprite3D* selectedRing_ = nullptr;
    cocos2d::Sprite3D* checkRing_ = nullptr;
    cocos2d::Label* statusLabel_ = nullptr;
    cocos2d::Label* endLabel_ = nullptr;
    cocos2d::EventListenerTouchOneByOne* touchListener_ = nullptr;
    std::vector<cocos2d::Sprite3D*> pulseMarkers_;  // markers with breathing pulse
};
