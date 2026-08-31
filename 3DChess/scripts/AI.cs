using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Xiangqi3D;

/// <summary>Negamax with alpha-beta, quiescence search (captures at the horizon), MVV-LVA ordering,
/// iterative deepening under a time cap. Timed-out depths are discarded so only completed results count.</summary>
public static class AI
{
    private static readonly int[] Value = { 0, 100000, 110, 110, 400, 900, 450, 100 };
    private const int MateScore = 100000;
    private static readonly Random Rng = new(20260831);

    public readonly struct Result
    {
        public readonly Move Move;
        public readonly int Score, Depth;
        public readonly long Nodes;
        public Result(Move m, int s, int d, long n) { Move = m; Score = s; Depth = d; Nodes = n; }
    }

    public static Result Search(int[] cells, Side turn, int msCap = 900)
    {
        var sw = Stopwatch.StartNew();
        var rootMoves = Rules.LegalMoves(cells, turn);
        if (rootMoves.Count == 0) return new Result(default, -MateScore, 0, 0);
        OrderMoves(cells, rootMoves);

        Move best = rootMoves[0];
        int bestScore = int.MinValue, depthDone = 0;
        long nodes = 0;
        List<(Move M, int S)> lastScored = null;

        for (int depth = 1; depth <= 5; depth++)
        {
            if (sw.ElapsedMilliseconds > msCap * 0.4) break;

            bool timedOut = false;
            int alpha = int.MinValue;
            var scored = new List<(Move M, int S)>();
            foreach (var m in rootMoves)
            {
                int captured = cells[m.To];
                cells[m.To] = cells[m.From];
                cells[m.From] = 0;
                int score = -Negamax(cells, Opp(turn), depth - 1, int.MinValue, -alpha, msCap, sw, 1, ref nodes, ref timedOut);
                cells[m.From] = cells[m.To];
                cells[m.To] = captured;
                if (timedOut) break; // partial depth is discarded
                scored.Add((m, score));
                if (score > alpha) alpha = score;
            }
            if (timedOut || scored.Count == 0) break; // keep the previous completed depth
            scored.Sort((a, b) => b.S.CompareTo(a.S));
            best = scored[0].M;
            bestScore = scored[0].S;
            depthDone = depth;
            lastScored = scored;
            rootMoves = scored.ConvertAll(t => t.M);
            if (bestScore > MateScore - 1000) break; // mate found
        }

        // variety: among near-equal best moves from the deepest completed search, pick at random
        if (lastScored != null)
        {
            var top = lastScored.FindAll(t => t.S >= bestScore - 10);
            if (top.Count > 1) best = top[Rng.Next(top.Count)].M;
        }

        return new Result(best, bestScore, depthDone, nodes);
    }

    private static Side Opp(Side s) => s == Side.Red ? Side.Black : Side.Red;

    private static int Negamax(int[] cells, Side side, int depth, int alpha, int beta, int msCap, Stopwatch sw, int ply, ref long nodes, ref bool timedOut)
    {
        nodes++;
        if (sw.ElapsedMilliseconds > msCap) { timedOut = true; return 0; }

        var moves = Rules.LegalMoves(cells, side);
        if (moves.Count == 0) return -MateScore + ply; // checkmate or stalemate: the side to move loses
        if (depth <= 0) return Quiesce(cells, side, alpha, beta, msCap, sw, ply, ref nodes, ref timedOut);

        OrderMoves(cells, moves);
        int best = int.MinValue;
        foreach (var m in moves)
        {
            int captured = cells[m.To];
            cells[m.To] = cells[m.From];
            cells[m.From] = 0;
            int score = -Negamax(cells, Opp(side), depth - 1, -beta, -alpha, msCap, sw, ply + 1, ref nodes, ref timedOut);
            cells[m.From] = cells[m.To];
            cells[m.To] = captured;
            if (timedOut) return 0;

            if (score > best) best = score;
            if (best > alpha) alpha = best;
            if (alpha >= beta) break;
        }
        return best;
    }

    /// <summary>Resolve hanging captures at the horizon: stand-pat or search captures (all moves while in check).</summary>
    private static int Quiesce(int[] cells, Side side, int alpha, int beta, int msCap, Stopwatch sw, int ply, ref long nodes, ref bool timedOut)
    {
        nodes++;
        if (sw.ElapsedMilliseconds > msCap) { timedOut = true; return 0; }

        bool inCheck = Rules.InCheck(cells, side);
        if (!inCheck)
        {
            int stand = Evaluate(cells, side);
            if (stand >= beta) return stand;
            if (stand > alpha) alpha = stand;
        }

        var moves = Rules.LegalMoves(cells, side);
        if (moves.Count == 0) return -MateScore + ply;
        OrderMoves(cells, moves);

        int best = inCheck ? int.MinValue : alpha;
        foreach (var m in moves)
        {
            if (!inCheck && cells[m.To] == 0) continue; // captures only when not in check
            int captured = cells[m.To];
            cells[m.To] = cells[m.From];
            cells[m.From] = 0;
            int score = -Quiesce(cells, Opp(side), -beta, -alpha, msCap, sw, ply + 1, ref nodes, ref timedOut);
            cells[m.From] = cells[m.To];
            cells[m.To] = captured;
            if (timedOut) return 0;

            if (score >= beta) return score;
            if (score > best) best = score;
            if (best > alpha) alpha = best;
        }
        return inCheck && best == int.MinValue ? -MateScore + ply : best;
    }

    /// <summary>MVV-LVA: most valuable victim first, least valuable attacker of equal victims.</summary>
    private static void OrderMoves(int[] cells, List<Move> moves) =>
        moves.Sort((a, b) =>
            (Value[Math.Abs(cells[b.To])] * 16 - Value[Math.Abs(cells[a.From])] / 8)
                .CompareTo(Value[Math.Abs(cells[a.To])] * 16 - Value[Math.Abs(cells[b.From])] / 8));

    /// <summary>Material + positional terms, from the given side's perspective.</summary>
    public static int Evaluate(int[] cells, Side side)
    {
        int score = 0; // red perspective
        int redGuards = 0, blackGuards = 0;
        for (int i = 0; i < 90; i++)
        {
            int p = cells[i];
            if (p == 0) continue;
            int t = Math.Abs(p);
            int s = Math.Sign(p);
            int f = i % 9, r = i / 9;
            int v = Value[t];

            switch ((PieceType)t)
            {
                case PieceType.Soldier:
                {
                    bool red = s > 0;
                    bool crossed = red ? r >= 5 : r <= 4;
                    if (crossed) v += 45;
                    int advance = red ? r - 3 : 6 - r;
                    if (advance > 0) v += advance * 6;
                    if (crossed && f >= 3 && f <= 5) v += 8;
                    break;
                }
                case PieceType.Horse:
                    if (f >= 2 && f <= 6 && r >= 2 && r <= 7) v += 10;
                    break;
                case PieceType.Chariot:
                    if (r >= 4 && r <= 5) v += 10;
                    break;
                case PieceType.Advisor:
                case PieceType.Elephant:
                    if (s > 0) redGuards++; else blackGuards++;
                    if (s > 0 ? r <= 1 : r >= 8) v += 8;
                    // palace-centre advisors form the solid 联防 shape
                    if (t == (int)PieceType.Advisor && f == 4 && (r == 1 || r == 8)) v += 10;
                    break;
            }
            score += s * v;
        }
        score += redGuards * 6 - blackGuards * 6; // king safety: keep the palace guard alive
        return side == Side.Red ? score : -score;
    }
}
