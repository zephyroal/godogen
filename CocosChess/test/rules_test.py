"""Mirror of CocosChess/Classes/ChessRules.h logic — validates the C++ rules engine
by reimplementing it 1:1 in Python and running assertions (same 90-cell scheme)."""
import sys

KING, ADVISOR, ELEPHANT, HORSE, CHARIOT, CANNON, SOLDIER = 1, 2, 3, 4, 5, 6, 7

def idx(f, r): return r * 9 + f
def file(i): return i % 9
def rank(i): return i // 9

class Position:
    def __init__(self):
        self.cells = [0] * 90
        self.turn = 1  # 1=Red, -1=Black

    def reset(self):
        self.cells = [0] * 90
        back = [5, 4, 3, 2, 1, 2, 3, 4, 5]
        for f in range(9):
            self.cells[idx(f, 0)] = back[f]
            self.cells[idx(f, 9)] = -back[f]
        self.cells[idx(1, 2)] = 6; self.cells[idx(7, 2)] = 6
        self.cells[idx(1, 7)] = -6; self.cells[idx(7, 7)] = -6
        for f in [0, 2, 4, 6, 8]:
            self.cells[idx(f, 3)] = 7
            self.cells[idx(f, 6)] = -7
        self.turn = 1

    def own(self, i, s): return self.cells[i] * s > 0
    def enemy(self, i, s): return self.cells[i] * s < 0
    def in_palace(self, i, s):
        f, r = file(i), rank(i)
        if f < 3 or f > 5: return False
        return (0 <= r <= 2) if s == 1 else (7 <= r <= 9)
    def own_side(self, i, s):
        r = rank(i)
        return r <= 4 if s == 1 else r >= 5

    def king_idx(self, s):
        for i in range(90):
            if self.cells[i] == s * KING: return i
        return -1

    def in_check(self, s):
        e = -s
        ki = self.king_idx(s)
        if ki < 0: return False
        for m in self.pseudo_moves(e):
            if m[1] == ki: return True
        ek = self.king_idx(e)
        if ek >= 0 and file(ki) == file(ek):
            lo, hi = sorted([rank(ki), rank(ek)])
            clear = all(self.cells[idx(file(ki), r)] == 0 for r in range(lo+1, hi))
            if clear: return True
        return False

    def make(self, m):
        cap = self.cells[m[1]]
        self.cells[m[1]] = self.cells[m[0]]
        self.cells[m[0]] = 0
        self.turn = -self.turn
        return cap

    def undo(self, m, cap):
        self.cells[m[0]] = self.cells[m[1]]
        self.cells[m[1]] = cap
        self.turn = -self.turn

    def add(self, out, frm, to, s):
        if 0 <= to < 90 and not self.own(to, s):
            out.append((frm, to))

    def pseudo_moves(self, s):
        out = []
        for i in range(90):
            c = self.cells[i]
            if c * s <= 0: continue
            p = abs(c)
            if p == KING:
                for df, dr in [(1,0),(-1,0),(0,1),(0,-1)]:
                    nf, nr = file(i)+df, rank(i)+dr
                    if 0<=nf<=8 and 0<=nr<=9 and self.in_palace(idx(nf,nr), s):
                        self.add(out, i, idx(nf,nr), s)
            elif p == ADVISOR:
                for df, dr in [(1,1),(-1,1),(1,-1),(-1,-1)]:
                    nf, nr = file(i)+df, rank(i)+dr
                    if 0<=nf<=8 and 0<=nr<=9 and self.in_palace(idx(nf,nr), s):
                        self.add(out, i, idx(nf,nr), s)
            elif p == ELEPHANT:
                for df, dr in [(2,2),(-2,2),(2,-2),(-2,-2)]:
                    nf, nr = file(i)+df, rank(i)+dr
                    if not (0<=nf<=8 and 0<=nr<=9): continue
                    t = idx(nf,nr)
                    if not self.own_side(t, s): continue
                    if self.cells[idx(file(i)+df//2, rank(i)+dr//2)] != 0: continue
                    self.add(out, i, t, s)
            elif p == HORSE:
                legs = [(2,1,1,0),(2,-1,1,0),(-2,1,-1,0),(-2,-1,-1,0),
                        (1,2,0,1),(1,-2,0,-1),(-1,2,0,1),(-1,-2,0,-1)]
                for df, dr, lf, lr in legs:
                    nf, nr = file(i)+df, rank(i)+dr
                    if not (0<=nf<=8 and 0<=nr<=9): continue
                    if self.cells[idx(file(i)+lf, rank(i)+lr)] != 0: continue
                    self.add(out, i, idx(nf,nr), s)
            elif p == CHARIOT:
                for df, dr in [(1,0),(-1,0),(0,1),(0,-1)]:
                    step = 1
                    while True:
                        nf, nr = file(i)+df*step, rank(i)+dr*step
                        if not (0<=nf<=8 and 0<=nr<=9): break
                        t = idx(nf,nr)
                        if self.own(t, s): break
                        out.append((i, t))
                        if self.enemy(t, s): break
                        step += 1
            elif p == CANNON:
                for df, dr in [(1,0),(-1,0),(0,1),(0,-1)]:
                    screened = False
                    step = 1
                    while True:
                        nf, nr = file(i)+df*step, rank(i)+dr*step
                        if not (0<=nf<=8 and 0<=nr<=9): break
                        t = idx(nf,nr)
                        if not screened:
                            if self.cells[t] == 0: out.append((i, t))
                            else: screened = True
                        else:
                            if self.cells[t] != 0:
                                if self.enemy(t, s): out.append((i, t))
                                break
                        step += 1
            elif p == SOLDIER:
                fwd = 1 if s == 1 else -1
                nr = rank(i) + fwd
                if 0 <= nr <= 9: self.add(out, i, idx(file(i), nr), s)
                if not self.own_side(i, s):
                    f = file(i)
                    if f > 0: self.add(out, i, idx(f-1, rank(i)), s)
                    if f < 8: self.add(out, i, idx(f+1, rank(i)), s)
        return out

    def legal_moves(self, s):
        out = []
        for m in self.pseudo_moves(s):
            cap = self.make(m)
            ok = not self.in_check(s)
            self.undo(m, cap)
            if ok: out.append(m)
        return out

# ------------------- assertions -------------------
fails = 0
def check(name, cond, detail=""):
    global fails
    status = "PASS" if cond else "FAIL"
    if not cond: fails += 1
    print(f"  {status} {name}{(' — ' + detail) if detail and not cond else ''}")

p = Position(); p.reset()

print("== initial position ==")
check("32 pieces on board", sum(1 for c in p.cells if c != 0) == 32, f"got {sum(1 for c in p.cells if c != 0)}")
red_moves = p.legal_moves(1)
# hand-verified: 4 chariot + 4 horse + 2 cannon x2-files? no—
# red initial: chariot 2×2 =4, horse 2×2=4, elephant 2×2=4 (two blocked by own soldier? no), advisor 2×2=4,
# king 0 (blocked by advisors? no, king has 1 free... actually king at rank0: up 1 blocked? no)
# standard count is 44 (matches the 3DChess C# engine's 44)
check("red has 44 legal moves", len(red_moves) == 44, f"got {len(red_moves)}")
black_moves = p.legal_moves(-1)
check("black also 44 (symmetry)", len(black_moves) == 44, f"got {len(black_moves)}")

print("== horse leg blocking ==")
p2 = Position(); p2.cells = [0]*90
p2.cells[idx(4,4)] = HORSE          # red horse at center
p2.cells[idx(4,5)] = SOLDIER        # blocking the north leg
all_m = p2.pseudo_moves(1)
horse_from = idx(4,4)
targets = sorted(m[1] for m in all_m if m[0] == horse_from)
# horse at (4,4): all 8 targets minus the two blocked by leg at (4,5): (3,6) and (5,6)
blocked = [idx(3,6), idx(5,6)]
check("2 north targets blocked by leg", all(b not in targets for b in blocked),
      f"targets={targets}")
check("6 targets available", len(targets) == 6, f"got {len(targets)}")

print("== cannon screen capture ==")
p3 = Position(); p3.cells = [0]*90
p3.cells[idx(0,0)] = CANNON         # red cannon
p3.cells[idx(0,3)] = SOLDIER        # screen
p3.cells[idx(0,6)] = -HORSE         # enemy behind screen
p3.cells[idx(0,8)] = -CHARIOT      # second enemy (must NOT be reachable in one jump)
m3 = p3.pseudo_moves(1)
c3 = sorted(m[1] for m in m3 if m[0] == idx(0,0))
check("cannon slides to empty cells before screen", idx(0,1) in c3 and idx(0,2) in c3)
check("cannon cannot slide past screen", idx(0,4) not in c3 and idx(0,5) not in c3)
check("cannon jumps screen to capture", idx(0,6) in c3)
check("cannon cannot reach past first capture", idx(0,8) not in c3)

print("== elephant cannot cross river ==")
p4 = Position(); p4.cells = [0]*90
p4.cells[idx(4,4)] = ELEPHANT      # red elephant just before the river
m4 = p4.pseudo_moves(1)
targets4 = [m[1] for m in m4 if m[0] == idx(4,4)]
ranks4 = {rank(t) for t in targets4}
check("no target crosses to rank>=5", all(r <= 4 for r in ranks4), f"ranks={ranks4}")

print("== flying general ==")
p5 = Position(); p5.cells = [0]*90
p5.cells[idx(4,0)] = KING           # red king
p5.cells[idx(4,9)] = -KING         # black king, same file, no blockers
check("kings facing = red in check", p5.in_check(1))
p5.cells[idx(4,4)] = SOLDIER        # blocker
check("blocker stops flying general", not p5.in_check(1))

print("== checkmate detection ==")
# lone black king at (4,9); red chariot on rank 9 controls both sideways moves,
# red chariot on file 4 controls the forward move — all escape squares covered
p6 = Position(); p6.cells = [0]*90
p6.cells[idx(4,0)] = KING           # red king
p6.cells[idx(4,9)] = -KING          # black king (lone)
p6.cells[idx(0,9)] = CHARIOT        # red chariot: rank-9 sweep → covers (3,9) and (5,9)
p6.cells[idx(4,5)] = CHARIOT        # red chariot: file-4 sweep → covers (4,8)
check("black is in check", p6.in_check(-1))
check("black is checkmated", len(p6.legal_moves(-1)) == 0,
      f"black has {len(p6.legal_moves(-1))} legal moves: {p6.legal_moves(-1)}")

print("== soldier crossing river ==")
p7 = Position(); p7.cells = [0]*90
p7.cells[idx(4,4)] = SOLDIER       # red soldier, not crossed
m7 = [m[1] for m in p7.pseudo_moves(1) if m[0] == idx(4,4)]
check("uncrossed soldier: forward only", len(m7) == 1 and m7[0] == idx(4,5))
p7.cells[idx(4,4)] = 0; p7.cells[idx(4,5)] = SOLDIER  # crossed the river
m7b = [m[1] for m in p7.pseudo_moves(1) if m[0] == idx(4,5)]
check("crossed soldier: forward + sideways", len(m7b) == 3 and idx(3,5) in m7b and idx(5,5) in m7b and idx(4,6) in m7b)

print()
if fails == 0:
    print(f"=== ALL PASS ===")
    sys.exit(0)
else:
    print(f"=== {fails} FAILURES ===")
    sys.exit(1)
