"""
세븐(Sevens) AI 전략 검증 시뮬레이터.

unity-client 의 게임 규칙과 전략을 그대로 옮겨, AI vs AI 대전으로
'어떤 전략 레버가 승률에 영향을 주는가'를 측정한다.

핵심 발견:
  1) 카드 선택(최대화/최소화/무작위)은 승률에 무관 — 모두 ~25.9%.
  2) 유일하게 의미 있는 레버는 "낼 수 있는 만큼 다 내느냐" — 1장만 내고 멈추면 6%로 폭락.
  3) 따라서 난이도 = '추가 장 놓침 확률(overlookChance)'로 구현.
     Easy(0.55) → 사람 승률 35.9%, Normal(0.20) → 30.0%, Hard(0.0/완벽) → 25.0%.

실행: python3 ai_sim.py
"""
import random

MINN, MAXN, SEVEN = 1, 13, 7
SUITS = range(4)


class Line:
    def __init__(self, suit):
        self.suit = suit
        self.mn = SEVEN
        self.mx = SEVEN  # 7이 미리 깔린 상태

    def can(self, c):
        su, n = c
        if su != self.suit:
            return False
        return (n == self.mn - 1 and self.mn > MINN) or (n == self.mx + 1 and self.mx < MAXN)

    def place(self, c):
        n = c[1]
        self.mn = min(self.mn, n)
        self.mx = max(self.mx, n)


class Field:
    def __init__(self):
        self.lines = [Line(i) for i in SUITS]

    def can(self, c):
        return self.lines[c[0]].can(c)

    def place(self, c):
        self.lines[c[0]].place(c)

    def playable(self, hand):
        return [c for c in hand if self.can(c)]

    def has_play(self, hand):
        return any(self.can(c) for c in hand)


# ── 전략들 ──────────────────────────────────────────────────────────
def random_ai(rng, overlook=0.0):
    """난이도 레버: 추가 장(plays>=1)에서 overlook 확률로 놓쳐 조기 종료. 첫 장은 의무."""
    def choose(field, hand, ctx):
        p = field.playable(hand)
        if not p:
            return None
        if ctx['plays'] >= 1 and overlook > 0 and rng.random() < overlook:
            return None
        return rng.choice(p)
    return choose


def stop_after_one(rng):
    def choose(field, hand, ctx):
        if ctx['plays'] >= 1:
            return None
        p = field.playable(hand)
        return rng.choice(p) if p else None
    return choose


def dfs_throughput_ai(rng, minimize=False):
    """이번 턴 처리량을 정확히 DFS로 계산해 첫 수를 최대/최소화."""
    def first_move_values(field, hand, max_plays):
        ln = {i: (field.lines[i].mn, field.lines[i].mx) for i in SUITS}

        def playable(state, hh):
            r = []
            for c in hh:
                m0, m1 = state[c[0]]
                n = c[1]
                if (n == m0 - 1 and m0 > MINN) or (n == m1 + 1 and m1 < MAXN):
                    r.append(c)
            return r

        def dfs(state, hh, depth):
            if depth == 0:
                return 0
            ps = playable(state, hh)
            if not ps:
                return 0
            best = 0
            for c in ps:
                m0, m1 = state[c[0]]
                n = c[1]
                ns = dict(state)
                ns[c[0]] = (min(m0, n), max(m1, n))
                nh = [x for x in hh if x != c]
                best = max(best, 1 + dfs(ns, nh, depth - 1))
            return best

        res = {}
        for c in playable(ln, hand):
            m0, m1 = ln[c[0]]
            n = c[1]
            ns = dict(ln)
            ns[c[0]] = (min(m0, n), max(m1, n))
            nh = [x for x in hand if x != c]
            res[c] = 1 + dfs(ns, nh, max_plays - 1)
        return res

    def choose(field, hand, ctx):
        p = field.playable(hand)
        if not p:
            return None
        res = first_move_values(field, hand, 3 - ctx['plays'])
        target = min(res.values()) if minimize else max(res.values())
        return rng.choice([c for c, v in res.items() if v == target])
    return choose


# ── 게임 ────────────────────────────────────────────────────────────
def deal(rng):
    deck = [(su, n) for su in SUITS for n in range(1, 14) if n != SEVEN]
    rng.shuffle(deck)
    return [sorted(deck[i * 12:(i + 1) * 12]) for i in range(4)]


def play_game(strats, rng, maxturns=4000):
    hands = deal(rng)
    field = Field()
    cur = 0
    consec_pass = 0
    for _ in range(maxturns):
        hand = hands[cur]
        plays = 0
        while plays < 3:  # 턴당 최대 3장
            ctx = {'plays': plays, 'opp': [len(hands[i]) for i in range(4) if i != cur]}
            c = strats[cur](field, hand, ctx)
            # 의무 제출 가드: 첫 장은 낼 카드가 있으면 반드시 낸다.
            if c is None and plays == 0 and field.has_play(hand):
                c = field.playable(hand)[0]
            if c is None or not field.can(c):
                break
            field.place(c)
            hand.remove(c)
            plays += 1
            if not hand:
                return cur
        consec_pass = consec_pass + 1 if plays == 0 else 0
        if consec_pass >= 4:
            return -1  # 교착(의무 제출 규칙상 사실상 발생 안 함)
        cur = (cur + 1) % 4
    return -1


def winrate_seat0(seat0_factory, base_factory, N=4000, base_seed=1000):
    wins = 0
    for g in range(N):
        rng = random.Random(base_seed + g)
        strats = [seat0_factory(rng)] + [base_factory(rng) for _ in range(3)]
        if play_game(strats, rng) == 0:
            wins += 1
    return wins / N


if __name__ == '__main__':
    print("== (1) 카드 선택은 승률 무관: seat0 vs 무작위 3명, 4000판 ==")
    rows = [
        ("무작위", lambda r: random_ai(r)),
        ("처리량 최대(DFS-MAX)", lambda r: dfs_throughput_ai(r, False)),
        ("처리량 최소(DFS-MIN)", lambda r: dfs_throughput_ai(r, True)),
        ("1장만 내고 멈춤", stop_after_one),
    ]
    for name, fac in rows:
        wr = winrate_seat0(fac, lambda r: random_ai(r))
        print(f"  {name:22s} {wr*100:5.2f}%")

    print("\n== (2) 난이도 구배: 완벽 1명 vs 상대 3명(해당 난이도), 6000판 ==")
    diffs = [("Easy(0.55)", 0.55), ("Normal(0.20)", 0.20), ("Hard(0.0/완벽)", 0.0)]
    for name, ov in diffs:
        wr = winrate_seat0(lambda r: random_ai(r, 0.0),
                           lambda r, ov=ov: random_ai(r, ov),
                           N=6000, base_seed=7000)
        print(f"  상대 {name:16s} → 사람 승률 {wr*100:5.2f}%")
