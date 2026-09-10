// AIPlayer.h — negamax with alpha-beta pruning and MVV-LVA ordering
#pragma once
#include "ChessRules.h"
#include <atomic>
#include <chrono>
#include <algorithm>
#include <future>

namespace chess {

class AIPlayer {
public:
    explicit AIPlayer(Side s) : side_(s) {}

    // Search the given position for the best move. Time budget in ms (soft limit).
    Move Search(const Position& pos, int timeBudgetMs, int& outDepth, long long& outNodes) {
        nodes_ = 0;
        deadline_ = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeBudgetMs);
        Move best{};
        best.from = -1;

        for (int depth = 2; depth <= 6; depth++) {
            int score;
            Move m = AlphaBeta(pos, depth, -INF, INF, side_, score);
            if (m.from < 0) break;
            best = m;
            outDepth = depth;
            if (score > INF - 100) break; // found a winning capture line
            if (TimeUp()) break;
        }
        outNodes = nodes_.load();
        if (best.from < 0) { // fallback: first legal move
            auto moves = pos.LegalMoves(side_);
            if (!moves.empty()) best = moves[0];
        }
        return best;
    }

    static constexpr int INF = 100000;

private:
    Side side_;
    std::atomic<long long> nodes_{ 0 };
    std::chrono::steady_clock::time_point deadline_;

    bool TimeUp() const {
        return std::chrono::steady_clock::now() > deadline_;
    }

    static int PieceValue(int p) {
        switch ((PieceType)std::abs(p)) {
            case PieceType::King: return 10000;
            case PieceType::Chariot: return 900;
            case PieceType::Cannon: return 450;
            case PieceType::Horse: return 400;
            case PieceType::Elephant: return 200;
            case PieceType::Advisor: return 200;
            case PieceType::Soldier: return 100;
            default: return 0;
        }
    }

    // simple material + mobility evaluation, from the given side's perspective
    int Evaluate(const Position& pos, Side s) const {
        int score = 0;
        for (int i = 0; i < 90; i++) {
            int c = pos.cells[i];
            if (c == 0) continue;
            int v = PieceValue(c);
            // small bonus for soldiers past the river
            int r = Rank(i);
            if ((PieceType)std::abs(c) == PieceType::Soldier) {
                bool crossed = (c > 0) ? (r >= 5) : (r <= 4);
                if (crossed) v += 20;
            }
            score += (c > 0) ? v : -v;
        }
        return (s == Side::Red) ? score : -score;
    }

    Move AlphaBeta(const Position& pos, int depth, int alpha, int beta, Side s, int& outScore) {
        if (TimeUp() && depth < 4) { outScore = Evaluate(pos, s); return { -1, -1 }; }
        nodes_++;

        if (depth == 0) {
            outScore = Evaluate(pos, s);
            return { -1, -1 };
        }
        if (pos.IsCheckmate(s)) { outScore = -INF + (10 - depth); return { -1, -1 }; }
        if (pos.IsStalemate(s)) { outScore = 0; return { -1, -1 }; }

        auto moves = pos.PseudoMoves(s);
        // order: captures first, biggest victim / smallest attacker first (MVV-LVA)
        std::sort(moves.begin(), moves.end(), [&](const Move& a, const Move& b) {
            int va = std::abs(pos.cells[a.to]), vb = std::abs(pos.cells[b.to]);
            if (va != vb) return va > vb;
            if (va == 0) return false;
            int fa = std::abs(pos.cells[a.from]), fb = std::abs(pos.cells[b.from]);
            return fa < fb;
        });

        Move best{ -1, -1 };
        int bestScore = -INF;
        for (auto& m : moves) {
            if (Own(pos, m.to, s)) continue;
            Position tmp = pos;
            int cap;
            tmp.MakeMove(m, cap);
            if (tmp.InCheck(s)) continue; // not a legal move

            int score;
            Side next = (s == Side::Red) ? Side::Black : Side::Red;
            AlphaBeta(tmp, depth - 1, -beta, -alpha, next, score);
            score = -score;

            if (score > bestScore) {
                bestScore = score;
                best = m;
            }
            if (score > alpha) alpha = score;
            if (alpha >= beta) break;
        }
        if (best.from < 0 && moves.empty()) {
            // handled by IsCheckmate/IsStalemate above, but be safe
            outScore = pos.InCheck(s) ? -INF + (10 - depth) : 0;
            return { -1, -1 };
        }
        outScore = bestScore;
        return best;
    }

    static bool Own(const Position& pos, int idx, Side s) {
        return s == Side::Red ? pos.cells[idx] > 0 : pos.cells[idx] < 0;
    }
};

} // namespace chess
