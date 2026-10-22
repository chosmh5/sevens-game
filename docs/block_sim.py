"""
봉쇄(전략적 보류) 전략 검증 — ai_sim.py 의 규칙·게임 루프를 그대로 쓴다. (2026-10-22)

질문: "낼 수 있어도 안 내는 게 전략일 수 있지 않나?"
6월 ai_sim.py 는 '무작위로 멈추기'와 '1장만 내고 멈추기'만 시험했다.
여기서는 상황을 보고 골라서 쥐는 전략(봉쇄)을 시험한다.

AI 가 쓰는 정보: 자기 손패와 필드뿐. 7을 뺀 48장이 모두 분배되므로
'내 손에 없고 필드에 없는 카드' = 반드시 상대 중 누군가의 손에 있다.

봉쇄 전략
  - 이번 턴에 손패를 다 비울 수 있으면 비운다(승리).
  - '안전한' 카드 = 내도 상대에게 새 카드를 열어 주지 않는 카드(다음 장이 내 것이거나 A/K 끝).
  - 추가 장(2·3번째): 안전한 카드만 낸다. 없으면 쥐고 턴 종료.
  - 첫 장(의무): 안전한 카드 중 내 연속이 긴 것. 없으면 그 방향에 내 카드가 많이 남은 줄을 연다.

결과(15,000판): 봉쇄 1명 vs '다 내기' 3명 → 봉쇄 승률 약 81% (공정 몫 25%)
  - 고르기만 영리하게(보류 X) 28%, 보류만(고르기 X) 34% → 둘을 합쳐야 크게 이긴다.
  - 사람이 '다 내기'로 봉쇄 AI 3명을 상대하면 사람 승률 약 9%, 사람도 봉쇄하면 약 22%.

실행: python3 block_sim.py [판 수]
"""
import random, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ai_sim import random_ai, stop_after_one, play_game, MINN, MAXN


def _dir(field, c):
    s, n = c
    return 1 if n == field.lines[s].mx + 1 else -1

def safe(field, hand, c):
    """내도 상대에게 새 카드를 열어 주지 않음 = 다음 장이 내 것이거나 끝(A/K)."""
    s, n = c
    m = n + _dir(field, c)
    return not (MINN <= m <= MAXN) or (s, m) in hand

def my_run(field, hand, c):
    """c 다음부터 그 방향으로 내 손에 연속으로 있는 장 수."""
    s, n = c
    d, k, m, hs = _dir(field, c), 0, n + _dir(field, c), set(hand)
    while MINN <= m <= MAXN and (s, m) in hs:
        k += 1; m += d
    return k

def my_beyond(field, hand, c):
    """c 방향으로 내 손에 남은 카드 수(연속 아니어도)."""
    s, n = c
    d = _dir(field, c)
    return sum(1 for (hs, hn) in hand if hs == s and (hn - n) * d > 0)

def finish_move(field, hand, left):
    """남은 left 장 안에 손패를 다 비울 수 있으면 그 첫 카드, 아니면 None."""
    if len(hand) > left:
        return None
    st = {s: (field.lines[s].mn, field.lines[s].mx) for s in range(4)}
    def can(st, c):
        m0, m1 = st[c[0]]; n = c[1]
        return (n == m0 - 1 and m0 > MINN) or (n == m1 + 1 and m1 < MAXN)
    def put(st, c):
        m0, m1 = st[c[0]]; ns = dict(st); ns[c[0]] = (min(m0, c[1]), max(m1, c[1])); return ns
    def ok(st, hh):
        return not hh or any(can(st, c) and ok(put(st, c), [x for x in hh if x != c]) for c in hh)
    for c in hand:
        if can(st, c) and ok(put(st, c), [x for x in hand if x != c]):
            return c
    return None

def blocker(rng, hold=True, smart_first=True):
    def choose(field, hand, ctx):
        p = field.playable(hand)
        if not p:
            return None
        f = finish_move(field, hand, 3 - ctx['plays'])
        if f:
            return f
        good = [c for c in p if safe(field, hand, c)]
        key = lambda c: (my_run(field, hand, c), rng.random())
        if ctx['plays'] >= 1:
            if not hold:
                return max(good, key=key) if good else rng.choice(p)
            return max(good, key=key) if good else None
        if not smart_first:
            return rng.choice(p)
        if good:
            return max(good, key=key)
        return max(p, key=lambda c: (my_beyond(field, hand, c), rng.random()))
    return choose


def seat0_winrate(seat0, others, N, seed):
    w = 0
    for g in range(N):
        rng = random.Random(seed + g)
        if play_game([seat0(rng)] + [others(rng) for _ in range(3)], rng) == 0:
            w += 1
    return w / N


if __name__ == '__main__':
    N = int(sys.argv[1]) if len(sys.argv) > 1 else 15000
    dump = lambda r: random_ai(r, 0.0)
    pct = lambda x: f"{x*100:5.1f}%"

    print(f"== (A) 한 명만 전략을 바꿈, 나머지 3명은 '다 내기'(현재 Hard) · {N}판 ==")
    for name, fac in [("다 내기 (현재 Hard, 기준)", dump),
                      ("1장만 내고 멈춤 (6월 실험)", stop_after_one),
                      ("고르기만 영리하게 (보류 X)", lambda r: blocker(r, hold=False)),
                      ("보류만 (고르기 X)", lambda r: blocker(r, smart_first=False)),
                      ("봉쇄 (고르기 + 보류)", blocker)]:
        print(f"  {name:26s} {pct(seat0_winrate(fac, dump, N, 1000))}")

    print(f"\n== (B) 사람 승률 — AI 3명의 전략별 · {N}판 ==")
    for name, ai in [("Easy (55% 무작위로 멈춤)", lambda r: random_ai(r, 0.55)),
                     ("Normal (20%)", lambda r: random_ai(r, 0.20)),
                     ("Hard (다 내기)", dump),
                     ("봉쇄", blocker)]:
        print(f"  AI = {name:22s} 사람 다 내기 {pct(seat0_winrate(dump, ai, N, 3000))}"
              f" · 사람 봉쇄 {pct(seat0_winrate(blocker, ai, N, 3000))}")
