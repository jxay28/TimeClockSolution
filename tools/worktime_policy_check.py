#!/usr/bin/env python3
"""
Verifica regressioni Fase 2.4 su 5 scenari business:
1) giornata normale
2) giornata spezzata
3) turno notturno cross-day
4) giornata festiva
5) deficit con recupero a blocchi

Nota: lo script replica la policy implementata nel Core (WorkTimeCalculator)
per controllo rapido in assenza di build .NET sul server.
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, date
from math import ceil, floor


@dataclass
class Policy:
    rounding_block: int = 15
    overtime_threshold: int = 15
    overtime_block: int = 15
    recovery_block: int = 15


def round_up(dt: datetime, block: int) -> datetime:
    if block <= 0:
        return dt
    m = dt.hour * 60 + dt.minute
    r = int(ceil(m / block) * block)
    return dt.replace(hour=0, minute=0, second=0, microsecond=0) + __import__("datetime").timedelta(minutes=r)


def round_down(dt: datetime, block: int) -> datetime:
    if block <= 0:
        return dt
    m = dt.hour * 60 + dt.minute
    r = int(floor(m / block) * block)
    return dt.replace(hour=0, minute=0, second=0, microsecond=0) + __import__("datetime").timedelta(minutes=r)


def pair_cross_day(punches: list[tuple[datetime, str]]) -> list[tuple[datetime, datetime]]:
    punches = sorted(punches, key=lambda x: x[0])
    open_in = None
    out = []
    for ts, kind in punches:
        if kind.lower() == "entrata":
            open_in = ts
        elif kind.lower() == "uscita" and open_in is not None and ts > open_in:
            out.append((open_in, ts))
            open_in = None
    return out


def calc_day(day: date, pairs: list[tuple[datetime, datetime]], expected_minutes: int, policy: Policy, is_holiday: bool = False):
    rounded = []
    for i, o in pairs:
        ri = round_up(i, policy.rounding_block)
        ro = round_down(o, policy.rounding_block)
        if ro < ri:
            ro = ri
        rounded.append((ri, ro))

    worked = sum(max(0, int(round((o - i).total_seconds() / 60))) for i, o in rounded)

    if is_holiday:
        return {
            "ordinary": 0,
            "overtime": worked,
            "worked": worked,
            "recovery": 0,
        }

    if worked >= expected_minutes:
        extra = worked - expected_minutes
        if extra < policy.overtime_threshold:
            overtime = 0
        elif policy.overtime_block <= 0:
            overtime = extra
        else:
            overtime = (extra // policy.overtime_block) * policy.overtime_block
        return {
            "ordinary": expected_minutes,
            "overtime": overtime,
            "worked": worked,
            "recovery": 0,
        }

    deficit = expected_minutes - worked
    recovery = int(ceil(deficit / policy.recovery_block) * policy.recovery_block) if policy.recovery_block > 0 else deficit
    ordinary = max(0, worked - recovery)
    return {
        "ordinary": ordinary,
        "overtime": 0,
        "worked": worked,
        "recovery": recovery,
    }


def hm(s: str) -> tuple[int, int]:
    h, m = s.split(":")
    return int(h), int(m)


def dt(d: str, t: str) -> datetime:
    h, m = hm(t)
    return datetime.fromisoformat(d).replace(hour=h, minute=m, second=0, microsecond=0)


def run():
    p = Policy()
    expected = 8 * 60

    scenarios = []

    # 1) Normale: 08:00-12:00 + 13:00-17:00 => 8h ordinarie, 0 extra
    s1 = [(dt("2026-02-01", "08:00"), "Entrata"), (dt("2026-02-01", "12:00"), "Uscita"), (dt("2026-02-01", "13:00"), "Entrata"), (dt("2026-02-01", "17:00"), "Uscita")]
    scenarios.append(("normale", date(2026, 2, 1), s1, False, {"ordinary": 480, "overtime": 0}))

    # 2) Spezzato con extra piccolo: 08:02-12:01 + 13:03-17:12 -> arrotondato 08:15-12:00 + 13:15-17:00 = 7h30 => deficit
    s2 = [(dt("2026-02-02", "08:02"), "Entrata"), (dt("2026-02-02", "12:01"), "Uscita"), (dt("2026-02-02", "13:03"), "Entrata"), (dt("2026-02-02", "17:12"), "Uscita")]
    scenarios.append(("spezzato", date(2026, 2, 2), s2, False, {"ordinary": 420, "overtime": 0}))

    # 3) Notturno cross-day: 22:00 -> 06:00 = 8h ordinarie
    s3 = [(dt("2026-02-03", "22:00"), "Entrata"), (dt("2026-02-04", "06:00"), "Uscita")]
    scenarios.append(("notturno", date(2026, 2, 3), s3, False, {"ordinary": 480, "overtime": 0}))

    # 4) Festivo: tutto straordinario
    s4 = [(dt("2026-02-07", "08:00"), "Entrata"), (dt("2026-02-07", "12:00"), "Uscita")]
    scenarios.append(("festivo", date(2026, 2, 7), s4, True, {"ordinary": 0, "overtime": 240}))

    # 5) Deficit: 08:00-15:40 -> arrotondato 08:00-15:30 = 450 min, deficit 30,
    # recupero a blocchi 30 => ordinarie 420
    s5 = [(dt("2026-02-05", "08:00"), "Entrata"), (dt("2026-02-05", "15:40"), "Uscita")]
    scenarios.append(("deficit", date(2026, 2, 5), s5, False, {"ordinary": 420, "overtime": 0}))

    ok = True
    for name, d, punches, holiday, expected_out in scenarios:
        pairs = pair_cross_day(punches)
        got = calc_day(d, pairs, expected, p, holiday)
        if got["ordinary"] != expected_out["ordinary"] or got["overtime"] != expected_out["overtime"]:
            ok = False
            print(f"[FAIL] {name}: got ord={got['ordinary']} ot={got['overtime']} expected ord={expected_out['ordinary']} ot={expected_out['overtime']}")
        else:
            print(f"[OK]   {name}: ord={got['ordinary']} ot={got['overtime']} worked={got['worked']} rec={got['recovery']}")

    if not ok:
        raise SystemExit(1)


if __name__ == "__main__":
    run()
