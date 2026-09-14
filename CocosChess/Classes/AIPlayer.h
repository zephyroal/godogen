// AIPlayer.h — negamax with alpha-beta, quiescence search, MVV-LVA ordering,
// iterative deepening under a time cap with partial depths discarded, and
// near-equal move variety — mirroring the Godot 3DChess AI (AI.cs).
#pragma once
#include "ChessRules.h"
#include <atomic>
#include <chrono>
#include <algorithm>
#include <future>
#include <random>
#include <vector>

namespace chess {

class AIPlayer {
public:
    explicit AIPlayer(Side s) : side_(s) {}

    // Search the given position for the best move. Time budget in ms (soft limit).
    Move Search(const Position& pos, int timeBudgetMs, int& outDepth, long long& outNodes) {
        outDepth = 0;
        outNodes = 0;
        nodes_ = 0;
        start_ = std::chrono::steady_clock::now();
        deadline_ = start_ + std::chrono::milliseconds(timeBudgetMs);

        Position work = pos;
        auto rootMoves = work.LegalMoves(side_);
        if (rootMoves.empty()) return { -1, -1 };
        OrderMoves(work, rootMoves);

        Move best = rootMoves[0];
        int bestScore = 0;
        std::vector<std::pair<Move, int>> lastScored;

        for (int depth = 1; depth <= 8; depth++) {
            if (ElapsedMs() > timeBudgetMs * 0.4) break; // not enough time for a deeper pass

            bool timedOut = false;
            int alpha = -INF_SCORE;
            std::vector<std::pair<Move, int>> scored;
            for (auto& m : rootMoves) {
                int captured;
                work.MakeMove(m, captured);
                bool childTimeout = false;
                int score = -Negamax(work, Opp(side_), depth - 1, -INF_SCORE, -alpha, 1, childTimeout);
                work.UndoMove(m, captured);
                if (childTimeout) { timedOut = true; break; } // partial depth is discarded
                scored.emplace_back(m, score);
                if (score > alpha) alpha = score;
            }
            if (timedOut || scored.empty()) break; // keep the previous completed depth

            std::stable_sort(scored.begin(), scored.end(),
                [](const std::pair<Move, int>& a, const std::pair<Move, int>& b) {
                    return a.second > b.second;
                });
            best = scored[0].first;
            bestScore = scored[0].second;
            outDepth = depth;
            lastScored = scored;
            rootMoves.clear();
            for (auto& t : scored) rootMoves.push_back(t.first);
            if (bestScore > MATE_SCORE - 1000) break; // mate found
        }
        outNodes = nodes_.load();

        // variety: among near-equal best moves from the deepest completed search, pick at random
        if (!lastScored.empty()) {
            std::vector<Move> top;
            for (auto& t : lastScored)
                if (t.second >= bestScore - 10) top.push_back(t.first);
            if (top.size() > 1) {
                std::uniform_int_distribution<int> dist(0, (int)top.size() - 1);
                best = top[dist(rng_)];
            }
        }
        return best;
    }

    static constexpr int MATE_SCORE = 100000;

private:
    static constexpr int INF_SCORE = 1 << 20; // bounds: |score| never exceeds MATE_SCORE + ply

    Side side_;
    std::atomic<long long> nodes_{ 0 };
    std::chrono::steady_clock::time_point start_, deadline_;
    std::mt19937 rng_{ 20260831 };

    static Side Opp(Side s) { return s == Side::Red ? Side::Black : Side::Red; }

    long ElapsedMs() const {
        return (long)std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - start_).count();
    }

    bool TimeUp() const {
        return std::chrono::steady_clock::now() > deadline_;
    }

    // PieceType values — Godot AI.cs Value[]: 帅100000/士110/象110/马400/车900/炮450/兵100
    static int PieceValue(int p) {
        switch ((PieceType)std::abs(p)) {
            case PieceType::King:     return 100000;
            case PieceType::Advisor:  return 110;
            case PieceType::Elephant: return 110;
            case PieceType::Horse:    return 400;
            case PieceType::Chariot:  return 900;
            case PieceType::Cannon:   return 450;
            case PieceType::Soldier:  return 100;
            default:                  return 0;
        }
    }

    // Material + positional terms, from the given side's perspective (AI.cs Evaluate)
    static int Evaluate(const Position& pos, Side s) {
        int score = 0; // red perspective
        int redGuards = 0, blackGuards = 0;
        for (int i = 0; i < 90; i++) {
            int p = pos.cells[i];
            if (p == 0) continue;
            int t = std::abs(p);
            int sign = (p > 0) ? 1 : -1;
            int f = File(i), r = Rank(i);
            int v = PieceValue(p);

            switch ((PieceType)t) {
                case PieceType::Soldier: {
                    bool red = sign > 0;
                    bool crossed = red ? (r >= 5) : (r <= 4);
                    if (crossed) v += 45;
                    int advance = red ? (r - 3) : (6 - r);
                    if (advance > 0) v += advance * 6;
                    if (crossed && f >= 3 && f <= 5) v += 8;
                    break;
                }
                case PieceType::Horse:
                    if (f >= 2 && f <= 6 && r >= 2 && r <= 7) v += 10;
                    break;
                case PieceType::Chariot:
                    if (r >= 4 && r <= 5) v += 10;
                    break;
                case PieceType::Advisor:
                case PieceType::Elephant:
                    if (sign > 0) redGuards++; else blackGuards++;
                    if (sign > 0 ? (r <= 1) : (r >= 8)) v += 8;
                    // palace-centre advisors form the solid defensive shape
                    if (t == (int)PieceType::Advisor && f == 4 && (r == 1 || r == 8)) v += 10;
                    break;
                default:
                    break;
            }
            score += sign * v;
        }
        score += redGuards * 6 - blackGuards * 6; // king safety: keep the palace guard alive
        return (s == Side::Red) ? score : -score;
    }

    // MVV-LVA: most valuable victim first, least valuable attacker of equal victims.
    static void OrderMoves(const Position& pos, std::vector<Move>& moves) {
        std::stable_sort(moves.begin(), moves.end(), [&](const Move& a, const Move& b) {
            int ka = PieceValue(pos.cells[a.to]) * 16 - PieceValue(pos.cells[a.from]) / 8;
            int kb = PieceValue(pos.cells[b.to]) * 16 - PieceValue(pos.cells[b.from]) / 8;
            return ka > kb;
        });
    }

    int Negamax(Position& pos, Side s, int depth, int alpha, int beta, int ply, bool& timedOut) {
        nodes_++;
        if (TimeUp()) { timedOut = true; return 0; }

        auto moves = pos.LegalMoves(s);
        if (moves.empty()) return -MATE_SCORE + ply; // checkmate or stalemate: the side to move loses
        if (depth <= 0) return Quiesce(pos, s, alpha, beta, ply, timedOut);

        OrderMoves(pos, moves);
        int best = -INF_SCORE;
        for (auto& m : moves) {
            int captured;
            pos.MakeMove(m, captured);
            bool childTimeout = false;
            int score = -Negamax(pos, Opp(s), depth - 1, -beta, -alpha, ply + 1, childTimeout);
            pos.UndoMove(m, captured);
            if (childTimeout) { timedOut = true; return 0; }

            if (score > best) best = score;
            if (best > alpha) alpha = best;
            if (alpha >= beta) break;
        }
        return best;
    }

    // Resolve hanging captures at the horizon: stand-pat or search captures
    // (all moves while in check). (AI.cs Quiesce)
    int Quiesce(Position& pos, Side s, int alpha, int beta, int ply, bool& timedOut) {
        nodes_++;
        if (TimeUp()) { timedOut = true; return 0; }

        bool inCheck = pos.InCheck(s);
        if (!inCheck) {
            int stand = Evaluate(pos, s);
            if (stand >= beta) return stand;
            if (stand > alpha) alpha = stand;
        }

        auto moves = pos.LegalMoves(s);
        if (moves.empty()) return -MATE_SCORE + ply;
        OrderMoves(pos, moves);

        int best = inCheck ? -INF_SCORE : alpha;
        for (auto& m : moves) {
            if (!inCheck && pos.cells[m.to] == 0) continue; // captures only when not in check
            int captured;
            pos.MakeMove(m, captured);
            bool childTimeout = false;
            int score = -Quiesce(pos, Opp(s), -beta, -alpha, ply + 1, childTimeout);
            pos.UndoMove(m, captured);
            if (childTimeout) { timedOut = true; return 0; }

            if (score >= beta) return score;
            if (score > best) best = score;
            if (best > alpha) alpha = best;
        }
        return (inCheck && best == -INF_SCORE) ? -MATE_SCORE + ply : best;
    }
};

} // namespace chess
