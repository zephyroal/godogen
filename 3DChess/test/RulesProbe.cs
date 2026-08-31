using Godot;
using System.Collections.Generic;

namespace Xiangqi3D;

/// <summary>
/// Headless rules verification: `godot --headless --script test/RulesProbe.cs`
/// Asserts the full rules engine against hand-verified cases (counts are filtered to the piece under test,
/// since legality requires both kings on the board). Exits non-zero on any failure.
/// </summary>
public partial class RulesProbe : SceneTree
{
    private int _pass, _fail;

    public override void _Initialize()
    {
        TestInitialMoveCount();
        TestSymmetry();
        TestHorseLeg();
        TestElephantEye();
        TestCannonScreen();
        TestFlyingGeneral();
        TestSoldierCrossing();
        TestCheckmate();
        TestStalemate();
        TestUndo();

        GD.Print($"=== RULES PROBE: {_pass} passed, {_fail} failed ===");
        Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(bool ok, string name)
    {
        if (ok) { _pass++; GD.Print($"  PASS {name}"); }
        else { _fail++; GD.Print($"  FAIL {name}"); }
    }

    private static int[] Board()
    {
        var cells = new int[90];
        cells[Position.Idx(3, 0)] = (int)PieceType.King;   // red king
        cells[Position.Idx(5, 9)] = -(int)PieceType.King;   // black king (different file: no facing)
        return cells;
    }

    private static List<Move> FromIdx(List<Move> moves, int idx) => moves.FindAll(m => m.From == idx);

    private void TestInitialMoveCount()
    {
        var p = Position.Initial();
        var red = Rules.LegalMoves(p.Cells, Side.Red);
        // 车4 马4 象4 士2 帅1 炮24(12×2) 兵5 = 44 — matches the known xiangqi initial mobility
        GD.Print($"  initial red legal moves = {red.Count}");
        Check(red.Count == 44, "initial red = 44 legal moves (车4+马4+象4+士2+帅1+炮24+兵5)");
    }

    private void TestSymmetry()
    {
        var p = Position.Initial();
        var black = Rules.LegalMoves(p.Cells, Side.Black);
        Check(black.Count == 44, "initial black = 44 legal moves (symmetry)");
    }

    private void TestHorseLeg()
    {
        var cells = Board();
        cells[Position.Idx(4, 4)] = (int)PieceType.Horse;    // red horse center
        cells[Position.Idx(4, 5)] = (int)PieceType.Soldier;  // own piece = leg, blocks both north targets
        var moves = FromIdx(Rules.LegalMoves(cells, Side.Red), Position.Idx(4, 4));
        bool northBlocked = !moves.Exists(m => m.To == Position.Idx(3, 6)) && !moves.Exists(m => m.To == Position.Idx(5, 6));
        Check(moves.Count == 6 && northBlocked, "horse leg blocks north moves, 6 others legal");
    }

    private void TestElephantEye()
    {
        var cells = Board();
        cells[Position.Idx(4, 4)] = (int)PieceType.Elephant; // red elephant on its river bank
        cells[Position.Idx(3, 3)] = -(int)PieceType.Soldier; // eye blocks the (2,2) diagonal
        var moves = FromIdx(Rules.LegalMoves(cells, Side.Red), Position.Idx(4, 4));
        // targets: (2,2) eye-blocked; (6,2) open; (2,6)/(6,6) across the river = illegal
        Check(moves.Count == 1 && moves[0].To == Position.Idx(6, 2), "elephant: eye blocks one diagonal, river blocks two");
    }

    private void TestCannonScreen()
    {
        var cells = Board();
        cells[Position.Idx(4, 2)] = (int)PieceType.Cannon;   // red cannon
        cells[Position.Idx(4, 4)] = (int)PieceType.Soldier; // one screen (own)
        cells[Position.Idx(4, 6)] = -(int)PieceType.Soldier; // target behind exactly one screen
        cells[Position.Idx(4, 8)] = -(int)PieceType.Chariot; // target behind two screens
        var moves = FromIdx(Rules.LegalMoves(cells, Side.Red), Position.Idx(4, 2));
        Check(moves.Exists(m => m.To == Position.Idx(4, 6)), "cannon captures behind exactly one screen");
        Check(!moves.Exists(m => m.To == Position.Idx(4, 8)), "cannon cannot capture behind two screens");
        Check(moves.Exists(m => m.To == Position.Idx(4, 1)) && moves.Exists(m => m.To == Position.Idx(4, 3)),
            "cannon slides to empty squares before the screen");
    }

    private void TestFlyingGeneral()
    {
        var cells = new int[90];
        cells[Position.Idx(4, 0)] = (int)PieceType.King;   // red king
        cells[Position.Idx(4, 9)] = -(int)PieceType.King;   // black king on the same open file
        cells[Position.Idx(2, 5)] = (int)PieceType.Horse;   // red horse
        Check(Rules.InCheck(cells, Side.Red) && Rules.InCheck(cells, Side.Black), "kings facing on an open file = both in check");

        var legal = Rules.LegalMoves(cells, Side.Red);
        var horseMoves = FromIdx(legal, Position.Idx(2, 5));
        // only moves landing on file 4 break the facing; the horse reaches (4,4) and (4,6)
        Check(horseMoves.Count == 2 && horseMoves.Exists(m => m.To == Position.Idx(4, 4)) && horseMoves.Exists(m => m.To == Position.Idx(4, 6)),
            "only horse moves that break the facing are legal");
        Check(!legal.Exists(m => m.From == Position.Idx(4, 0) && m.To == Position.Idx(4, 1)),
            "king stepping along the open file stays facing = illegal");
    }

    private void TestSoldierCrossing()
    {
        var cells = Board();
        cells[Position.Idx(4, 4)] = (int)PieceType.Soldier; // red soldier on its own bank
        var home = FromIdx(Rules.LegalMoves(cells, Side.Red), Position.Idx(4, 4));
        Check(home.Count == 1 && home[0].To == Position.Idx(4, 5), "soldier before the river: forward only");

        cells[Position.Idx(4, 4)] = 0;
        cells[Position.Idx(4, 5)] = (int)PieceType.Soldier; // crossed
        var crossed = FromIdx(Rules.LegalMoves(cells, Side.Red), Position.Idx(4, 5));
        Check(crossed.Count == 3, "crossed soldier: forward + two sideways");

        cells[Position.Idx(4, 5)] = 0;
        cells[Position.Idx(4, 0)] = (int)PieceType.Soldier; // home rank
        var backRank = FromIdx(Rules.LegalMoves(cells, Side.Red), Position.Idx(4, 0));
        Check(backRank.Count == 1 && backRank[0].To == Position.Idx(4, 1), "soldier never moves backward");
    }

    private void TestCheckmate()
    {
        // black king alone at (4,9); red rook (4,7) checks along the file; rook (0,9) covers rank 9
        var cells = new int[90];
        cells[Position.Idx(4, 9)] = -(int)PieceType.King;
        cells[Position.Idx(4, 7)] = (int)PieceType.Chariot;
        cells[Position.Idx(0, 9)] = (int)PieceType.Chariot;

        Check(Rules.InCheck(cells, Side.Black), "double-rook position is check");
        var ok = Rules.IsGameOver(cells, Side.Black, out bool mate, out Side loser);
        Check(ok && mate && loser == Side.Black, "checkmate detected: no legal moves while in check");
    }

    private void TestStalemate()
    {
        // black king alone at (4,9), not in check, but every step is covered = 困毙
        var cells = new int[90];
        cells[Position.Idx(4, 9)] = -(int)PieceType.King;
        cells[Position.Idx(3, 8)] = (int)PieceType.Chariot; // covers (3,9) and (4,8)
        cells[Position.Idx(5, 8)] = (int)PieceType.Chariot; // covers (5,9) and (4,8)

        Check(!Rules.InCheck(cells, Side.Black), "stalemate position is not check");
        var ok = Rules.IsGameOver(cells, Side.Black, out bool mate, out Side loser);
        Check(ok && !mate && loser == Side.Black, "stalemate (困毙) detected: no moves, side to move loses");
    }

    private void TestUndo()
    {
        var p = Position.Initial();
        int before = p.Cells[Position.Idx(7, 2)];
        p.MakeMove(new Move(Position.Idx(7, 2), Position.Idx(4, 2))); // 炮二平五
        Check(p.Cells[Position.Idx(4, 2)] == before && p.Cells[Position.Idx(7, 2)] == 0 && p.Turn == Side.Black, "make move applies");
        p.UnmakeMove();
        Check(p.Cells[Position.Idx(7, 2)] == before && p.Cells[Position.Idx(4, 2)] == 0 && p.Turn == Side.Red, "unmake restores");
    }
}
