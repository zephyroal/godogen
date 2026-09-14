"""Assert the --verify stage dumps of CocosChess.

Each verify_<stage>.txt carries: cells line + scalar fields. Expected states
are derived from the scripted flow (see GameScene::RunVerifyFlow):
  炮二平五 (19->22) / 象3进5 (83->76) / 炮五进四 (22->58, captures 58) /
  象5退3 (76->83) / undos / rematch / menu.
"""
import os
import sys

BASE = r"D:/godogen/CocosChess"

back = [5, 4, 3, 2, 1, 2, 3, 4, 5]
INITIAL = [0] * 90
for f in range(9):
    INITIAL[f] = back[f]
    INITIAL[9 * 9 + f] = -back[f]
INITIAL[2 * 9 + 1] = 6
INITIAL[2 * 9 + 7] = 6
INITIAL[7 * 9 + 1] = -6
INITIAL[7 * 9 + 7] = -6
for f in (0, 2, 4, 6, 8):
    INITIAL[3 * 9 + f] = 7
    INITIAL[6 * 9 + f] = -7


def load(stage):
    path = os.path.join(BASE, "verify_%s.txt" % stage)
    lines = open(path, encoding="utf-8").read().splitlines()
    cells = [int(x) for x in lines[0].split()]
    fields = {}
    for line in lines[1:]:
        toks = line.split()
        for i in range(0, len(toks) - 1, 2):
            fields[toks[i]] = toks[i + 1]
    return cells, fields


def cells_after(moves, base=None):
    """Apply (from,to) list to a copy of INITIAL with captures recorded."""
    cells = list(INITIAL if base is None else base)
    captured = []
    for frm, to in moves:
        captured.append(cells[to])
        cells[to] = cells[frm]
        cells[frm] = 0
    return cells


FAILS = []


def check(name, cond, detail=""):
    print("[%s] %s%s" % ("PASS" if cond else "FAIL", name, (" — " + detail) if detail and not cond else ""))
    if not cond:
        FAILS.append(name)


# ---- stage expectations ----
S = load("01_start")
check("01 initial cells", S[0] == INITIAL)
check("01 turn Red", S[1]["turn"] == "Red")
check("01 moveCount 0", S[1]["moveCount"] == "0")
check("01 alive 32", S[1]["alive"] == "32")
check("01 no captures", S[1]["redCaptured"] == "0" and S[1]["blackCaptured"] == "0")
check("01 gameStarted", S[1]["gameStarted"] == "1")

S = load("02_cannon_center")
exp = cells_after([(19, 22)])
check("02 cells 炮二平五", S[0] == exp)
check("02 turn Black", S[1]["turn"] == "Black")
check("02 moveCount 1 / history 1", S[1]["moveCount"] == "1" and S[1]["history"] == "1")

S = load("03_elephant_screen")
exp = cells_after([(19, 22), (83, 76)])
check("03 cells 象3进5", S[0] == exp)
check("03 turn Red", S[1]["turn"] == "Red")
check("03 not in check", S[1]["inCheck"] == "0")

S = load("04_capture_check")
exp = cells_after([(19, 22), (83, 76), (22, 58)])
check("04 cells 炮五进四吃中卒", S[0] == exp)
check("04 turn Black", S[1]["turn"] == "Black")
check("04 alive 31", S[1]["alive"] == "31")
check("04 blackCaptured 1", S[1]["blackCaptured"] == "1")
check("04 black in check", S[1]["inCheck"] == "1")
check("04 no AI in two-player", S[1]["aiThinking"] == "0")
check("04 not animating at dump", S[1]["animating"] == "0")

S = load("06_check_answered")
exp = cells_after([(19, 22), (83, 76), (22, 58), (76, 83)])
check("06 cells 象5退3应将", S[0] == exp)
check("06 check answered", S[1]["inCheck"] == "0")
check("06 turn Red", S[1]["turn"] == "Red")
check("06 moveCount 4", S[1]["moveCount"] == "4")

S = load("07_undo1")  # pops the check answer -> state after m3
exp = cells_after([(19, 22), (83, 76), (22, 58)])
check("07 cells back to post-capture", S[0] == exp)
check("07 turn Black", S[1]["turn"] == "Black")
check("07 history 3", S[1]["history"] == "3")
check("07 alive still 31", S[1]["alive"] == "31")
check("07 blackCaptured still 1", S[1]["blackCaptured"] == "1")

S = load("08_undo2")  # pops the capture: pawn revives from the tray
exp = cells_after([(19, 22), (83, 76)])
check("08 cells back to pre-capture", S[0] == exp)
check("08 pawn revived at 58", S[0][58] == -7)
check("08 cannon back at 22", S[0][22] == 6)
check("08 alive back to 32", S[1]["alive"] == "32")
check("08 blackCaptured back to 0", S[1]["blackCaptured"] == "0")
check("08 turn Red", S[1]["turn"] == "Red")
check("08 history 2", S[1]["history"] == "2")

S = load("09a_undo3")  # pops the elephant screen
exp = cells_after([(19, 22)])
check("09a cells back to post-m1", S[0] == exp)
check("09a turn Black", S[1]["turn"] == "Black")
check("09a moveCount 1", S[1]["moveCount"] == "1")

S = load("09b_undo_all")
check("09b == initial cells", S[0] == INITIAL)
check("09b turn Red / history 0", S[1]["turn"] == "Red" and S[1]["history"] == "0")
check("09b alive 32 / no captures", S[1]["alive"] == "32" and S[1]["blackCaptured"] == "0")

S = load("10_rematch")
check("10 rematch == initial", S[0] == INITIAL)
check("10 gameStarted after rematch", S[1]["gameStarted"] == "1")
check("10 moveCount 0", S[1]["moveCount"] == "0")

S = load("11_menu")
check("11 menu == initial cells", S[0] == INITIAL)
check("11 gameStarted 0 at menu", S[1]["gameStarted"] == "0")

print("VERIFY_%s" % ("ALL_PASS" if not FAILS else "FAILS=%d: %s" % (len(FAILS), ", ".join(FAILS))))
sys.exit(1 if FAILS else 0)
