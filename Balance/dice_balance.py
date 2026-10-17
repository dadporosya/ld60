"""Poker dice odds and score balance for LD60.

Mirrors PokerDiceEvaluator.Contained (Assets/_PROJECT/Scripts/Dice/PokerDiceCombination.cs) and
DiceManager.Evaluate: a throw scores the best hand it contains, and every die adds its face value
(DiceManager's Add Dice Sum).

Play is solved exactly: before each reroll the player keeps (locks) the dice that maximise the
expected result, and can stop (Finish) at any time.

Run: python Balance/dice_balance.py   (rewrites the CSVs next to this file)
"""
import csv
import math
import os
from itertools import combinations_with_replacement

FACES = 6
DICE_COUNTS = (5, 6, 7)
MAX_REROLLS = 5
# The game's table (New Scene: 5 dice, 4 rerolls, 1 round) and LevelManager.pointsByRound before.
GAME_DICE, GAME_REROLLS = 5, 4
OLD_TARGETS = [10, 20, 30, 67]
HANDS = ["Nothing", "OnePair", "TwoPair", "ThreeOfAKind", "SmallStraight", "LargeStraight",
         "FullHouse", "FourOfAKind", "FiveOfAKind"]

# Hand bonus on top of the dice sum, from how hard each hand is to make (expected_throws, scaled so
# pair = 10 and two pair = pair + pair = 20), rounded to 5s. Full house = three + pair.
POINTS = {
    "Nothing": 0,
    "OnePair": 10,
    "TwoPair": 20,
    "ThreeOfAKind": 25,
    "SmallStraight": 30,
    "FullHouse": 35,
    "FourOfAKind": 40,
    "LargeStraight": 45,
    "FiveOfAKind": 65,
}
# The table before: hand points only, no dice sum.
OLD_POINTS = {"Nothing": 0, "OnePair": 5, "TwoPair": 10, "ThreeOfAKind": 15, "SmallStraight": 20,
              "LargeStraight": 30, "FullHouse": 25, "FourOfAKind": 40, "FiveOfAKind": 60}

OUT_DIR = os.path.dirname(os.path.abspath(__file__))


def contained(dice):
    counts = [0] * (FACES + 1)
    for v in dice:
        counts[v] += 1
    mx = max(counts)
    pairs = sum(1 for c in counts if c >= 2)
    has3, has2 = 3 in counts, 2 in counts
    run = best = 0
    for f in range(1, FACES + 1):
        run = run + 1 if counts[f] else 0
        best = max(best, run)
    res = {"Nothing"}
    if mx >= 2: res.add("OnePair")
    if pairs >= 2 or mx >= 4: res.add("TwoPair")
    if mx >= 3: res.add("ThreeOfAKind")
    if best >= 4: res.add("SmallStraight")
    if best >= 5: res.add("LargeStraight")
    if (has3 and has2) or mx >= 5: res.add("FullHouse")
    if mx >= 4: res.add("FourOfAKind")
    if mx >= 5: res.add("FiveOfAKind")
    return res


def best_hand(points, dice):
    return max(contained(dice), key=lambda h: (points[h], HANDS.index(h)))


def multisets(k):
    return list(combinations_with_replacement(range(1, FACES + 1), k))


def probability(combo):
    ways = math.factorial(len(combo))
    for f in set(combo):
        ways //= math.factorial(combo.count(f))
    return ways / FACES ** len(combo)


class Table:
    """Every state of an n-dice game: full throws, kept subsets, and the throws between them."""

    def __init__(self, n):
        self.n = n
        self.states = multisets(n)
        self.kept = [m for k in range(n + 1) for m in multisets(k)]
        state_index = {s: i for i, s in enumerate(self.states)}
        kept_index = {s: i for i, s in enumerate(self.kept)}
        rolls = {k: [(m, probability(m)) for m in multisets(k)] for k in range(n + 1)}
        # kept -> [(state, p)] when the rest are thrown
        self.throw = [[(state_index[tuple(sorted(kp + r))], p) for r, p in rolls[n - len(kp)]]
                      for kp in self.kept]
        # state -> every subset that can be kept (the full state = stop)
        self.subsets = []
        for s in self.states:
            subs = {()}
            for v in s:
                subs |= {tuple(sorted(x + (v,))) for x in subs}
            self.subsets.append(sorted((kept_index[x] for x in subs), key=lambda i: -len(self.kept[i])))
        self.full = [kept_index[s] for s in self.states]

    def solve(self, terminal, rerolls):
        """W[r][kept]: expected result of throwing the rest with r rerolls still to come."""
        term = [terminal(s) for s in self.states]
        V = term[:]
        W = []
        for r in range(rerolls + 1):
            Wr = [sum(p * V[i] for i, p in t) for t in self.throw]
            W.append(Wr)
            V = [max(term[i], max(Wr[k] for k in self.subsets[i])) for i in range(len(self.states))]
        return W

    def final_states(self, terminal, rerolls):
        """The throws optimal play ends on ({state: chance}), and the expected result."""
        W = self.solve(terminal, rerolls)
        ends = {}
        mass = {self.kept.index(()): 1.0}
        for r in range(rerolls, -1, -1):
            nxt = {}
            for k, m in mass.items():
                for i, p in self.throw[k]:
                    if r > 0:
                        # the first best choice keeps the most dice; stopping wins ties
                        keep = max(self.subsets[i], key=lambda x: W[r - 1][x])
                        if keep != self.full[i] and W[r - 1][keep] > terminal(self.states[i]) + 1e-12:
                            nxt[keep] = nxt.get(keep, 0.0) + m * p
                            continue
                    ends[i] = ends.get(i, 0.0) + m * p
            mass = nxt
        return ends, W[rerolls][self.kept.index(())]

    def final_hands(self, terminal, rerolls, points):
        """Which hand optimal play ends on, and the expected result."""
        ends, ev = self.final_states(terminal, rerolls)
        dist = {h: 0.0 for h in HANDS}
        for i, p in ends.items():
            dist[best_hand(points, self.states[i])] += p
        return ev, dist

    def pass_chance(self, terminal, rerolls, target):
        """Chance to score at least target when playing for exactly that."""
        return self.solve(lambda d: 1.0 if terminal(d) >= target else 0.0, rerolls)[rerolls][self.kept.index(())]

    def expected_throws(self, hand):
        """Throws needed on average to make hand, aiming only for it, with unlimited rerolls."""
        done = [hand in contained(s) for s in self.states]
        T = [0.0] * len(self.states)
        while True:
            E = [sum(p * T[i] for i, p in t) for t in self.throw]
            T2 = [0.0 if done[i] else 1.0 + min(E[k] for k in self.subsets[i]) for i in range(len(self.states))]
            if max(abs(a - b) for a, b in zip(T, T2)) < 1e-10:
                break
            T = T2
        return 1.0 + sum(p * T[i] for i, p in self.throw[self.kept.index(())])


def score(points, add_sum):
    return lambda d: max(points[h] for h in contained(d)) + (sum(d) if add_sum else 0)


def main():
    rows, ev_rows = [], []
    for n in DICE_COUNTS:
        table = Table(n)
        start = table.kept.index(())
        has = {h: 0.0 for h in HANDS}
        best = {h: 0.0 for h in HANDS}
        for s in table.states:
            p = probability(s)
            for h in contained(s):
                has[h] += p
            best[best_hand(POINTS, s)] += p

        go_for = {h: table.solve(lambda d, h=h: 1.0 if h in contained(d) else 0.0, MAX_REROLLS) for h in HANDS}
        old_W = table.solve(score(OLD_POINTS, False), MAX_REROLLS)

        finals = []
        for r in range(MAX_REROLLS + 1):
            ev, dist = table.final_hands(score(POINTS, True), r, POINTS)
            finals.append(dist)
            ev_old = old_W[r][start]
            ev_rows.append({"dice": n, "rerolls": r, "expected_points_old": round(ev_old, 2),
                            "expected_points_new": round(ev, 2), "new_over_old": round(ev / ev_old, 3)})

        for h in HANDS:
            row = {"dice": n, "hand": h, "points_old": OLD_POINTS[h], "bonus_new": POINTS[h],
                   "contains_1_throw_%": round(100 * has[h], 3),
                   "best_hand_1_throw_%": round(100 * best[h], 3)}
            for r in range(MAX_REROLLS + 1):
                row[f"go_for_{r}_rerolls_%"] = round(100 * go_for[h][r][start], 3)
            for r in range(MAX_REROLLS + 1):
                row[f"final_hand_{r}_rerolls_%"] = round(100 * finals[r][h], 3)
            rows.append(row)
        print(f"{n} dice done", flush=True)

    with open(os.path.join(OUT_DIR, "dice_hand_odds.csv"), "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    with open(os.path.join(OUT_DIR, "dice_expected_points.csv"), "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(ev_rows[0].keys()))
        w.writeheader()
        w.writerows(ev_rows)

    # --- Suggested hand bonus: difficulty (expected throws, 5 dice) scaled so pair 10, two pair 20 ---
    table = Table(GAME_DICE)
    throws = {h: table.expected_throws(h) for h in HANDS[1:]}
    k = math.log(2) / math.log(throws["TwoPair"] / throws["OnePair"])
    print(f"\nHand difficulty ({GAME_DICE} dice), points = 10 x (throws / pair throws)^{k:.3f}")
    for h in HANDS[1:]:
        print(f"  {h:14} {throws[h]:6.3f} throws -> {10 * (throws[h] / throws['OnePair']) ** k:5.1f}  (table: {POINTS[h]})")

    # --- Level targets: keep each one's pass chance (playing for the target) ---
    new_score = score(POINTS, True)
    old_score = score(OLD_POINTS, False)
    print(f"\nTargets ({GAME_DICE} dice, {GAME_REROLLS} rerolls):")
    target_rows = []
    for t in OLD_TARGETS:
        p_old = table.pass_chance(old_score, GAME_REROLLS, t)
        lo, hi = 0, 400
        while lo < hi:  # the highest new target that is at least as easy as the old one
            mid = (lo + hi + 1) // 2
            if table.pass_chance(new_score, GAME_REROLLS, mid) >= p_old - 1e-9: lo = mid
            else: hi = mid - 1
        p_new = table.pass_chance(new_score, GAME_REROLLS, lo)
        print(f"  old {t:3} ({100 * p_old:6.2f}%) -> new {lo:3} ({100 * p_new:6.2f}%)")
        target_rows.append({"old_target": t, "old_pass_%": round(100 * p_old, 2),
                            "new_target": lo, "new_pass_%": round(100 * p_new, 2)})
    with open(os.path.join(OUT_DIR, "dice_targets.csv"), "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(target_rows[0].keys()))
        w.writeheader()
        w.writerows(target_rows)

    for row in ev_rows:
        print(row)


if __name__ == "__main__":
    main()
