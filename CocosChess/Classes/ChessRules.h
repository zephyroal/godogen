// ChessRules.h — complete Xiangqi rules engine (pure logic, no cocos2d dependency)
#pragma once
#include <vector>
#include <cstring>
#include <cstdlib>
#include <algorithm>
#include <utility>

namespace chess {

enum class Side { Red = 1, Black = -1 };
enum class PieceType : int {
    None = 0, King = 1, Advisor = 2, Elephant = 3,
    Horse = 4, Chariot = 5, Cannon = 6, Soldier = 7,
};

struct Move {
    int from;   // 0..89, index = rank * 9 + file
    int to;
    bool operator==(const Move& o) const { return from == o.from && to == o.to; }
};

inline int Idx(int file, int rank) { return rank * 9 + file; }
inline int File(int idx) { return idx % 9; }
inline int Rank(int idx) { return idx / 9; }

// Cells: positive = Red, negative = Black, value = (int)PieceType
// Rank 0 = Red home (bottom of screen), rank 9 = Black home (top).
class Position {
public:
    int cells[90];
    Side turn;

    Position() { Reset(); }

    void Reset() {
        std::memset(cells, 0, sizeof(cells));
        const int back[9] = { 5, 4, 3, 2, 1, 2, 3, 4, 5 };
        for (int f = 0; f < 9; f++) cells[Idx(f, 0)] = back[f];       // red back rank
        cells[Idx(1, 2)] = 6; cells[Idx(7, 2)] = 6;                   // red cannons
        for (int f : {0, 2, 4, 6, 8}) cells[Idx(f, 3)] = 7;            // red soldiers
        for (int f = 0; f < 9; f++) cells[Idx(f, 9)] = -back[f];      // black back rank
        cells[Idx(1, 7)] = -6; cells[Idx(7, 7)] = -6;                  // black cannons
        for (int f : {0, 2, 4, 6, 8}) cells[Idx(f, 6)] = -7;          // black soldiers
        turn = Side::Red;
    }

    bool IsRed(int idx) const { return cells[idx] > 0; }
    bool IsBlack(int idx) const { return cells[idx] < 0; }
    bool Own(int idx, Side s) const { return s == Side::Red ? cells[idx] > 0 : cells[idx] < 0; }
    bool Enemy(int idx, Side s) const { return s == Side::Red ? cells[idx] < 0 : cells[idx] > 0; }
    bool InPalace(int idx, Side s) const {
        int f = File(idx), r = Rank(idx);
        if (f < 3 || f > 5) return false;
        return s == Side::Red ? (r >= 0 && r <= 2) : (r >= 7 && r <= 9);
    }
    bool OwnSide(int idx, Side s) const {
        int r = Rank(idx);
        return s == Side::Red ? r <= 4 : r >= 5;
    }

    int KingIdx(Side s) const {
        int sign = (s == Side::Red) ? 1 : -1;
        for (int i = 0; i < 90; i++)
            if (cells[i] == sign * (int)PieceType::King) return i;
        return -1;
    }

    bool InCheck(Side s) const {
        // a king is in check if any enemy pseudo-legal move captures it
        Side e = (s == Side::Red) ? Side::Black : Side::Red;
        int ki = KingIdx(s);
        if (ki < 0) return false;
        auto moves = PseudoMoves(e);
        for (auto& m : moves)
            if (m.to == ki) return true;
        // flying general: kings on same file with no pieces between
        int ek = KingIdx(e);
        if (ek >= 0 && File(ki) == File(ek)) {
            int r1 = Rank(ki), r2 = Rank(ek);
            if (r1 > r2) std::swap(r1, r2);
            bool clear = true;
            for (int r = r1 + 1; r < r2; r++)
                if (cells[Idx(File(ki), r)] != 0) { clear = false; break; }
            if (clear) return true;
        }
        return false;
    }

    void MakeMove(const Move& m, int& captured) {
        captured = cells[m.to];
        cells[m.to] = cells[m.from];
        cells[m.from] = 0;
        turn = (turn == Side::Red) ? Side::Black : Side::Red;
    }

    void UndoMove(const Move& m, int captured) {
        cells[m.from] = cells[m.to];
        cells[m.to] = captured;
        turn = (turn == Side::Red) ? Side::Black : Side::Red;
    }

    // All pseudo-legal moves for the given side (may leave own king in check).
    std::vector<Move> PseudoMoves(Side s) const {
        std::vector<Move> out;
        for (int i = 0; i < 90; i++) {
            if (!Own(i, s)) continue;
            int p = std::abs(cells[i]);
            switch ((PieceType)p) {
                case PieceType::King:    GenKing(i, s, out); break;
                case PieceType::Advisor: GenAdvisor(i, s, out); break;
                case PieceType::Elephant:GenElephant(i, s, out); break;
                case PieceType::Horse:  GenHorse(i, s, out); break;
                case PieceType::Chariot:GenChariot(i, s, out); break;
                case PieceType::Cannon: GenCannon(i, s, out); break;
                case PieceType::Soldier: GenSoldier(i, s, out); break;
                default: break;
            }
        }
        return out;
    }

    // Fully-legal moves: pseudo-legal minus those that leave own king in check.
    std::vector<Move> LegalMoves(Side s) const {
        std::vector<Move> out;
        for (auto& m : PseudoMoves(s)) {
            int cap;
            Position tmp = *this;
            tmp.MakeMove(m, cap);
            if (!tmp.InCheck(s)) out.push_back(m);
        }
        return out;
    }

    bool IsCheckmate(Side s) const { return InCheck(s) && LegalMoves(s).empty(); }
    bool IsStalemate(Side s) const { return !InCheck(s) && LegalMoves(s).empty(); }

private:
    static int RDir(Side s) { return s == Side::Red ? 1 : -1; } // forward rank direction

    void Add(int from, int to, Side s, std::vector<Move>& out) const {
        if (to < 0 || to >= 90) return;
        if (Own(to, s)) return;
        out.push_back({ from, to });
    }

    void GenKing(int i, Side s, std::vector<Move>& out) const {
        int f = File(i), r = Rank(i);
        const int dirs[4][2] = { {1,0},{-1,0},{0,1},{0,-1} };
        for (auto& d : dirs) {
            int nf = f + d[0], nr = r + d[1];
            int t = Idx(nf, nr);
            if (nf < 0 || nf > 8 || nr < 0 || nr > 9) continue;
            if (!InPalace(t, s)) continue;
            Add(i, t, s, out);
        }
    }

    void GenAdvisor(int i, Side s, std::vector<Move>& out) const {
        int f = File(i), r = Rank(i);
        const int dirs[4][2] = { {1,1},{-1,1},{1,-1},{-1,-1} };
        for (auto& d : dirs) {
            int nf = f + d[0], nr = r + d[1];
            if (nf < 0 || nf > 8 || nr < 0 || nr > 9) continue;
            int t = Idx(nf, nr);
            if (!InPalace(t, s)) continue;
            Add(i, t, s, out);
        }
    }

    void GenElephant(int i, Side s, std::vector<Move>& out) const {
        int f = File(i), r = Rank(i);
        const int dirs[4][2] = { {2,2},{-2,2},{2,-2},{-2,-2} };
        for (auto& d : dirs) {
            int nf = f + d[0], nr = r + d[1];
            if (nf < 0 || nf > 8 || nr < 0 || nr > 9) continue;
            int t = Idx(nf, nr);
            if (!OwnSide(t, s)) continue;                       // cannot cross river
            int eye = Idx(f + d[0] / 2, r + d[1] / 2);
            if (cells[eye] != 0) continue;                      // blocked by eye
            Add(i, t, s, out);
        }
    }

    void GenHorse(int i, Side s, std::vector<Move>& out) const {
        int f = File(i), r = Rank(i);
        const int dirs[8][4] = { // {df, dr, leg-df, leg-dr}
            { 2, 1, 1, 0}, { 2,-1, 1, 0}, {-2, 1,-1, 0}, {-2,-1,-1, 0},
            { 1, 2, 0, 1}, { 1,-2, 0,-1}, {-1, 2, 0, 1}, {-1,-2, 0,-1},
        };
        for (auto& d : dirs) {
            int nf = f + d[0], nr = r + d[1];
            if (nf < 0 || nf > 8 || nr < 0 || nr > 9) continue;
            int leg = Idx(f + d[2], r + d[3]);
            if (cells[leg] != 0) continue;                      // blocked by horse leg
            Add(i, Idx(nf, nr), s, out);
        }
    }

    void GenChariot(int i, Side s, std::vector<Move>& out) const {
        int f = File(i), r = Rank(i);
        const int dirs[4][2] = { {1,0},{-1,0},{0,1},{0,-1} };
        for (auto& d : dirs) {
            for (int step = 1; ; step++) {
                int nf = f + d[0] * step, nr = r + d[1] * step;
                if (nf < 0 || nf > 8 || nr < 0 || nr > 9) break;
                int t = Idx(nf, nr);
                if (Own(t, s)) break;
                out.push_back({ i, t });
                if (Enemy(t, s)) break;                        // capture and stop
            }
        }
    }

    void GenCannon(int i, Side s, std::vector<Move>& out) const {
        int f = File(i), r = Rank(i);
        const int dirs[4][2] = { {1,0},{-1,0},{0,1},{0,-1} };
        for (auto& d : dirs) {
            bool screened = false;
            for (int step = 1; ; step++) {
                int nf = f + d[0] * step, nr = r + d[1] * step;
                if (nf < 0 || nf > 8 || nr < 0 || nr > 9) break;
                int t = Idx(nf, nr);
                if (!screened) {
                    if (cells[t] == 0) { out.push_back({ i, t }); continue; }
                    screened = true;                            // first piece is the screen
                    continue;
                }
                if (cells[t] != 0) {                            // capture behind screen
                    if (Enemy(t, s)) out.push_back({ i, t });
                    break;
                }
            }
        }
    }

    void GenSoldier(int i, Side s, std::vector<Move>& out) const {
        int f = File(i), r = Rank(i);
        int forward = RDir(s);
        int nr = r + forward;
        if (nr >= 0 && nr <= 9) Add(i, Idx(f, nr), s, out);
        if (!OwnSide(i, s)) {                                   // crossed river: sideways
            if (f > 0) Add(i, Idx(f - 1, r), s, out);
            if (f < 8) Add(i, Idx(f + 1, r), s, out);
        }
    }
};

// Chinese character for each piece type per side
inline const char* PieceChar(PieceType t, Side s) {
    bool red = (s == Side::Red);
    switch (t) {
        case PieceType::King:    return red ? "帅" : "将";
        case PieceType::Advisor: return red ? "仕" : "士";
        case PieceType::Elephant:return red ? "相" : "象";
        case PieceType::Horse:   return "马";
        case PieceType::Chariot: return "车";
        case PieceType::Cannon:  return red ? "炮" : "砲";
        case PieceType::Soldier: return red ? "兵" : "卒";
        default: return "";
    }
}

} // namespace chess
