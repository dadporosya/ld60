"""Poker dice odds and score balance for LD60.

Mirrors PokerDiceEvaluator.Contained (Assets/_PROJECT/Scripts/Dice/PokerDiceCombination.cs) and
DiceManager.Evaluate: a throw scores the best hand it contains, and every die adds its face value
(DiceManager's Add Dice Sum).

Play is solved exactly: before each reroll the player keeps (locks) the dice that maximise the
expected result, and can stop (Finish) at any time. Level targets are solved the way the game plays a
level: several rounds whose points add up, the player playing for exactly the target, and Finish locked
until the level's total reaches it (until then every reroll is spent).

Run: python Balance/dice_balance.py   (rewrites the CSVs next to this file)
"""
import csv
import math
import os
from itertools import combinations_with_replacement

FACES = 6
DICE_COUNTS = (5, 6, 7)
MAX_REROLLS = 5
# The game's table (New Scene's DiceManager: 6 dice, 3 rerolls, 3 rounds a level, dice sum added).
GAME_DICE, GAME_REROLLS, GAME_ROUNDS = 6, 3, 3
# What LevelManager.pointsByRound was tuned for before (5 dice, 4 rerolls, 1 round, PREV_POINTS), and those
# targets. The new targets keep each one's pass chance.
PREV_DICE, PREV_REROLLS, PREV_ROUNDS = 5, 4, 1
PREV_TARGETS = [30, 47, 60, 105]
# A target out of reach without cards before (the final level) gets this pass chance instead: the one its
# 446 had on 5 dice.
OUT_OF_REACH_PASS = 0.0482
HANDS = ["Nothing", "OnePair", "TwoPair", "ThreeOfAKind", "SmallStraight", "LargeStraight",
         "FullHouse", "FourOfAKind", "FiveOfAKind", "TwoThreeOfAKind"]

# Hand bonus on top of the dice sum (the design sheet's bonus column; large straight as four of a kind).
POINTS = {
    "Nothing": 0,
    "OnePair": 10,
    "TwoPair": 20,
    "ThreeOfAKind": 30,
    "SmallStraight": 50,
    "FullHouse": 67,
    "FourOfAKind": 100,
    "LargeStraight": 100,
    "FiveOfAKind": 200,
    "TwoThreeOfAKind": 150,
}
# The bonus table before (also on top of the dice sum).
PREV_POINTS = {"Nothing": 0, "OnePair": 10, "TwoPair": 20, "ThreeOfAKind": 25, "SmallStraight": 30,
               "LargeStraight": 45, "FullHouse": 35, "FourOfAKind": 40, "FiveOfAKind": 65,
               "TwoThreeOfAKind": 0}  # not a hand back then: never the best one

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
    if sum(1 for c in counts if c >= 3) >= 2 or mx >= 6: res.add("TwoThreeOfAKind")
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

    def goal_chance(self, value, reached, rerolls):
        """
        Chance of a good end, value(state) being a final throw's worth (0..1). Stopping early is only
        allowed once reached(state) (the game's Finish unlocks at the target): before that every reroll
        is spent, rethrowing at least one die.
        """
        term = [value(s) for s in self.states]
        done = [reached(s) for s in self.states]
        V = term[:]
        for _ in range(rerolls):
            W = [sum(p * V[i] for i, p in t) for t in self.throw]
            V = [term[i] if done[i] else max(W[k] for k in self.subsets[i] if k != self.full[i])
                 for i in range(len(self.states))]
        return sum(p * V[i] for i, p in self.throw[self.kept.index(())])

    def level_pass_chances(self, points, rerolls, rounds):
        """P[t]: chance a level of `rounds` rounds adds up to at least t points, playing for exactly t."""
        scores = {s: score(points, True)(s) for s in self.states}
        top = max(scores.values()) * rounds
        P = [1.0] + [0.0] * top  # no rounds left: passed only if nothing is still needed

        def chance(P, need):
            return 1.0 if need <= 0 else (P[need] if need < len(P) else 0.0)

        for _ in range(rounds):
            prev = P
            P = [1.0] + [self.goal_chance(lambda s, n=n: chance(prev, n - scores[s]),
                                          lambda s, n=n: scores[s] >= n, rerolls)
                         for n in range(1, top + 1)]
        return P

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
        prev_W = table.solve(score(PREV_POINTS, True), MAX_REROLLS)

        finals = []
        for r in range(MAX_REROLLS + 1):
            ev, dist = table.final_hands(score(POINTS, True), r, POINTS)
            finals.append(dist)
            ev_prev = prev_W[r][start]
            ev_rows.append({"dice": n, "rerolls": r, "expected_points_prev": round(ev_prev, 2),
                            "expected_points_new": round(ev, 2), "new_over_prev": round(ev / ev_prev, 3)})

        for h in HANDS:
            row = {"dice": n, "hand": h, "bonus_prev": PREV_POINTS[h], "bonus_new": POINTS[h],
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
    print(f"\nTargets ({PREV_DICE}d {PREV_REROLLS}r x{PREV_ROUNDS} -> {GAME_DICE}d {GAME_REROLLS}r x{GAME_ROUNDS}):")
    p_prev = Table(PREV_DICE).level_pass_chances(PREV_POINTS, PREV_REROLLS, PREV_ROUNDS)
    p_new = table.level_pass_chances(POINTS, GAME_REROLLS, GAME_ROUNDS)
    target_rows = []
    for t in PREV_TARGETS:
        before = p_prev[t] if t < len(p_prev) else 0.0
        # the highest new target that is at least as easy as the old one
        goal = before if before > 0 else OUT_OF_REACH_PASS
        new = max(i for i, p in enumerate(p_new) if p >= goal - 1e-9)
        print(f"  prev {t:3} ({100 * before:6.2f}%) -> new {new:3} ({100 * p_new[new]:6.2f}%)")
        target_rows.append({"prev_target": t, "prev_pass_%": round(100 * before, 2),
                            "new_target": new, "new_pass_%": round(100 * p_new[new], 2)})
    with open(os.path.join(OUT_DIR, "dice_targets.csv"), "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(target_rows[0].keys()))
        w.writeheader()
        w.writerows(target_rows)
    with open(os.path.join(OUT_DIR, "dice_level_pass.csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["target", "pass_%"])
        w.writerows([t, round(100 * p, 2)] for t, p in enumerate(p_new) if p >= 0.0005)

    for row in ev_rows:
        print(row)


if __name__ == "__main__":
    main()
