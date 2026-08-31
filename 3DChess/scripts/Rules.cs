using System;
using System.Collections.Generic;

namespace Xiangqi3D;

public enum Side { Red, Black }

public enum PieceType { King = 1, Advisor = 2, Elephant = 3, Horse = 4, Chariot = 5, Cannon = 6, Soldier = 7 }

public readonly struct Move : IEquatable<Move>
{
    public readonly int From, To;
    public Move(int from, int to) { From = from; To = to; }
    public bool Equals(Move other) => From == other.From && To == other.To;
    public override bool Equals(object obj) => obj is Move m && Equals(m);
    public override int GetHashCode() => From * 97 + To;
    public override string ToString() => $"{From / 9},{From % 9}->{To / 9},{To % 9}";
}

/// <summary>Full Xiangqi position: 90 cells (index = rank*9+file, rank 0 = red home row) + side to move.</summary>
public sealed class Position
{
    public readonly int[] Cells = new int[90];
    public Side Turn = Side.Red;
    public readonly List<(Move Move, int Captured)> History = new();

    public static int Idx(int file, int rank) => rank * 9 + file;

    public static Position Initial()
    {
        var p = new Position();
        int[] back = { 5, 4, 3, 2, 1, 2, 3, 4, 5 }; // 车马象士将士象马车
        for (int f = 0; f < 9; f++) p.Cells[f] = back[f];
        p.Cells[Idx(1, 2)] = 6; p.Cells[Idx(7, 2)] = 6;
        foreach (int f in new[] { 0, 2, 4, 6, 8 }) p.Cells[Idx(f, 3)] = 7;
        for (int f = 0; f < 9; f++) p.Cells[Idx(f, 9)] = -back[f];
        p.Cells[Idx(1, 7)] = -6; p.Cells[Idx(7, 7)] = -6;
        foreach (int f in new[] { 0, 2, 4, 6, 8 }) p.Cells[Idx(f, 6)] = -7;
        return p;
    }

    public void MakeMove(Move m)
    {
        History.Add((m, Cells[m.To]));
        Cells[m.To] = Cells[m.From];
        Cells[m.From] = 0;
        Turn = Turn == Side.Red ? Side.Black : Side.Red;
    }

    public void UnmakeMove()
    {
        var (m, captured) = History[^1];
        History.RemoveAt(History.Count - 1);
        Cells[m.From] = Cells[m.To];
        Cells[m.To] = captured;
        Turn = Turn == Side.Red ? Side.Black : Side.Red;
    }
}

/// <summary>Complete rules: pseudo-legal generation, check (incl. flying general), legality filtering, mate/stalemate.</summary>
public static class Rules
{
    private static readonly (int F, int R)[] Ortho = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly (int F, int R)[] Diag = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
    private static readonly (int F, int R)[] Horse8 =
    {
        (1, 2), (-1, 2), (1, -2), (-1, -2), (2, 1), (2, -1), (-2, 1), (-2, -1),
    };

    public static bool InBoard(int f, int r) => f >= 0 && f < 9 && r >= 0 && r < 10;

    public static bool InPalace(int f, int r, Side side) =>
        f >= 3 && f <= 5 && (side == Side.Red ? r >= 0 && r <= 2 : r >= 7 && r <= 9);

    public static bool OwnSide(int r, Side side) => side == Side.Red ? r <= 4 : r >= 5;

    private static void Add(List<Move> list, int from, int[] cells, int nf, int nr)
    {
        if (!InBoard(nf, nr)) return;
        int t = cells[nr * 9 + nf];
        if (Math.Sign(t) == Math.Sign(cells[from])) return; // own piece
        list.Add(new Move(from, nr * 9 + nf));
    }

    public static List<Move> LegalMoves(int[] cells, Side side)
    {
        var pseudo = PseudoMoves(cells, side);
        var legal = new List<Move>(pseudo.Count);
        foreach (var m in pseudo)
        {
            int captured = cells[m.To];
            cells[m.To] = cells[m.From];
            cells[m.From] = 0;
            if (!InCheck(cells, side)) legal.Add(m);
            cells[m.From] = cells[m.To];
            cells[m.To] = captured;
        }
        return legal;
    }

    public static List<Move> PseudoMoves(int[] cells, Side side)
    {
        var list = new List<Move>(48);
        for (int i = 0; i < 90; i++)
            if (cells[i] != 0 && Math.Sign(cells[i]) == (side == Side.Red ? 1 : -1))
                GenFrom(cells, i, list);
        return list;
    }

    private static void GenFrom(int[] cells, int idx, List<Move> list)
    {
        int side = Math.Sign(cells[idx]);
        var s = side > 0 ? Side.Red : Side.Black;
        int f = idx % 9, r = idx / 9;

        switch ((PieceType)Math.Abs(cells[idx]))
        {
            case PieceType.King:
                foreach (var (df, dr) in Ortho)
                {
                    int nf = f + df, nr = r + dr;
                    if (InPalace(nf, nr, s)) Add(list, idx, cells, nf, nr);
                }
                break;

            case PieceType.Advisor:
                foreach (var (df, dr) in Diag)
                {
                    int nf = f + df, nr = r + dr;
                    if (InPalace(nf, nr, s)) Add(list, idx, cells, nf, nr);
                }
                break;

            case PieceType.Elephant:
                foreach (var (df, dr) in Diag)
                {
                    int nf = f + 2 * df, nr = r + 2 * dr;
                    if (!InBoard(nf, nr) || !OwnSide(nr, s)) continue;
                    if (cells[(r + dr) * 9 + f + df] != 0) continue; // 塞象眼
                    Add(list, idx, cells, nf, nr);
                }
                break;

            case PieceType.Horse:
                foreach (var (df, dr) in Horse8)
                {
                    int nf = f + df, nr = r + dr;
                    if (!InBoard(nf, nr)) continue;
                    int lf = Math.Abs(df) == 2 ? f + df / 2 : f; // 蹩马腿
                    int lr = Math.Abs(dr) == 2 ? r + dr / 2 : r;
                    if (cells[lr * 9 + lf] != 0) continue;
                    Add(list, idx, cells, nf, nr);
                }
                break;

            case PieceType.Chariot:
                foreach (var (df, dr) in Ortho)
                {
                    int nf = f + df, nr = r + dr;
                    while (InBoard(nf, nr))
                    {
                        int t = cells[nr * 9 + nf];
                        if (t == 0) { list.Add(new Move(idx, nr * 9 + nf)); }
                        else
                        {
                            if (Math.Sign(t) != side) list.Add(new Move(idx, nr * 9 + nf));
                            break;
                        }
                        nf += df; nr += dr;
                    }
                }
                break;

            case PieceType.Cannon:
                foreach (var (df, dr) in Ortho)
                {
                    int nf = f + df, nr = r + dr;
                    while (InBoard(nf, nr) && cells[nr * 9 + nf] == 0) // move to empty before the screen
                    {
                        list.Add(new Move(idx, nr * 9 + nf));
                        nf += df; nr += dr;
                    }
                    if (!InBoard(nf, nr)) continue;
                    nf += df; nr += dr; // hop over the screen, look for a capture target
                    while (InBoard(nf, nr))
                    {
                        int t = cells[nr * 9 + nf];
                        if (t != 0)
                        {
                            if (Math.Sign(t) != side) list.Add(new Move(idx, nr * 9 + nf)); // 翻山吃子
                            break;
                        }
                        nf += df; nr += dr;
                    }
                }
                break;

            case PieceType.Soldier:
                int fwd = side > 0 ? 1 : -1;
                Add(list, idx, cells, f, r + fwd);
                if (!OwnSide(r, s)) // crossed the river: may step sideways
                {
                    Add(list, idx, cells, f - 1, r);
                    Add(list, idx, cells, f + 1, r);
                }
                break;
        }
    }

    public static int FindKing(int[] cells, Side side)
    {
        int k = side == Side.Red ? 1 : -1;
        for (int i = 0; i < 90; i++)
            if (cells[i] == k) return i;
        return -1;
    }

    /// <summary>Side is in check: king attacked, or the two kings face each other on an open file.</summary>
    public static bool InCheck(int[] cells, Side side)
    {
        int k = FindKing(cells, side);
        if (k < 0) return true;
        int kf = k % 9, kr = k / 9;
        int enemySign = side == Side.Red ? -1 : 1;

        // orthogonal rays: first piece = enemy chariot / king (facing), second = enemy cannon
        foreach (var (df, dr) in Ortho)
        {
            int f = kf + df, r = kr + dr, stage = 0;
            while (InBoard(f, r))
            {
                int t = cells[r * 9 + f];
                if (t != 0)
                {
                    if (stage == 0)
                    {
                        int at = Math.Abs(t);
                        if (Math.Sign(t) == enemySign && (at == (int)PieceType.Chariot || at == (int)PieceType.King))
                            return true;
                        stage = 1;
                    }
                    else
                    {
                        if (Math.Sign(t) == enemySign && Math.Abs(t) == (int)PieceType.Cannon)
                            return true;
                        break;
                    }
                }
                f += df; r += dr;
            }
        }

        // horses: reverse-leg scan from the king
        foreach (var (df, dr) in Horse8)
        {
            int hf = kf + df, hr = kr + dr;
            if (!InBoard(hf, hr)) continue;
            if (cells[hr * 9 + hf] != enemySign * (int)PieceType.Horse) continue;
            int lf = Math.Abs(df) == 2 ? kf + df / 2 : kf;
            int lr = Math.Abs(dr) == 2 ? kr + dr / 2 : kr;
            if (cells[lr * 9 + lf] == 0) return true;
        }

        // soldiers: frontal attack, and sideways if the attacker crossed the river
        int fr = kr - enemySign; // square an enemy soldier would attack forward from
        if (InBoard(kf, fr) && cells[fr * 9 + kf] == enemySign * (int)PieceType.Soldier) return true;
        foreach (int sf in new[] { kf - 1, kf + 1 })
        {
            if (!InBoard(sf, kr)) continue;
            if (cells[kr * 9 + sf] != enemySign * (int)PieceType.Soldier) continue;
            if (!OwnSide(kr, side == Side.Red ? Side.Black : Side.Red)) return true; // soldier crossed
        }
        return false;
    }

    /// <summary>Game-over detection: no legal moves → the side to move loses (checkmate if in check, else 困毙 stalemate).</summary>
    public static bool IsGameOver(int[] cells, Side turn, out bool checkmate, out Side loser)
    {
        loser = turn;
        bool any = LegalMoves(cells, turn).Count > 0;
        checkmate = !any && InCheck(cells, turn);
        return !any;
    }
}
