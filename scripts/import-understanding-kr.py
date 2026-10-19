"""Build a separate Spanish KR reference from the cached official probability tables."""
import json
from decimal import Decimal
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
load = lambda path: json.loads((ROOT / path).read_text(encoding="utf-8-sig"))
legacy = load("public/data/game/encyclopedia/pet-progression.json")
options = load("research/dbaion/probability-race-options-resources.json")
grades = load("research/dbaion/probability-race-grades-resources.json")
assert options["pageNavigation"]["lastPage"] == grades["pageNavigation"]["lastPage"] == 1
families = {"지성": "Intellect", "야성": "Feral", "자연": "Nature", "변형": "Trans", "특수": "Special"}
qualities = {"일반": "Common", "희귀": "Rare", "전승": "Legend", "유일": "Unique", "영웅": "Epic"}
base = {
    "PVE 공격력": "PvEAddDamage", "PVE 방어력": "PvEDamageDefense", "PVE 피해 내성": "PvEDecreaseDamage", "PVE 피해 증폭": "PvEAmplifyDamage",
    "강타": "HardHit", "관통": "DefensePierce", "막기": "Block", "무기 피해 내성": "DecreaseWeaponDamage", "무기 피해 증폭": "AmplifyWeaponDamage",
    "보스 공격력": "BossNpcAddDamage", "봉혼석 추가 피해": "SealStoneAddDamage", "생명력": "HPMax", "완벽": "Perfect", "재생": "Restoration",
    "전방 공격력": "FrontAttackDamage", "전방 방어력": "FrontAttackDefense", "전방 치명타": "FrontAttackCritical", "전방 치명타 저항": "FrontAttackCriticalResist",
    "전방 피해 내성": "DecreaseFrontAttack", "전방 피해 증폭": "AmplifyFrontAttack", "정신력": "MPMax", "철벽": "IronWall", "최대 공격력": "WeaponDamage",
    "추가 공격력": "FixingDamage", "추가 명중": "Accuracy", "추가 방어력": "Defense", "추가 회피": "Evasion", "치명타": "Critical",
    "치명타 공격력": "CriticalAddDamage", "치명타 방어력": "CriticalDamageDefense", "치명타 저항": "CriticalResist", "치명타 피해 내성": "DecreaseCriticalDamage",
    "치명타 피해 증폭": "AmplifyCriticalDamage", "피해 내성": "DecreaseDamage", "피해 증폭": "AmplifyAllDamage",
    "후방 공격력": "BackAttackDamage", "후방 방어력": "BackAttackDefense", "후방 치명타": "BackAttackCritical", "후방 치명타 저항": "BackAttackCriticalResist",
    "후방 피해 내성": "DecreaseBackAttack", "후방 피해 증폭": "AmplifyBackAttack",
}
for name, prefix in families.items():
    for suffix, code in {"공격력": "Damage", "방어력": "Defense", "명중": "Accuracy", "회피": "Evasion", "치명타": "Critical", "치명타 저항": "CriticalResist", "막기": "Block", "피해 내성": "DecreaseDamage", "피해 증폭": "AmplifyDamage"}.items():
        base[name + "족 " + suffix] = prefix + code

types = []
percent_stats = set()
total = 0
for race in options["records"]:
    family = families[race["종족"]]
    old = next(t for t in legacy["types"] if t["id"] == family)
    type_data = {"id": family, "name": old["name"], "levels": [], "slots": []}
    grade_rows = next(r["@entries"] for r in grades["records"] if r["종족"] == race["종족"])
    for row in grade_rows:
        level = row["레벨"]
        xp = next(r[1] for r in old["levels"] if r[0] == level)
        probabilities = [float(row[k].rstrip("%")) for k in qualities] + [0]
        assert sum(probabilities) == 100
        type_data["levels"].append([level, xp, probabilities])
    for index, slot in enumerate(race["@entries"]):
        assert slot["슬롯"] == f"{index+1}번 슬롯"
        parsed = []
        for grade in slot["@entries"]:
            assert sum(Decimal(o["획득 확률"].rstrip("%")) for o in grade["@entries"]) == 100
            for option in grade["@entries"]:
                stat = base[option["옵션"]]  # Unknown translations fail closed.
                value = option["수치"]
                is_percent = "%" in value
                if is_percent:
                    percent_stats.add(stat)
                parts = value.replace("%", "").split("~")
                nums = [int(Decimal(p.strip()) * (100 if is_percent else 1)) for p in parts]
                lo, hi = nums[0], nums[-1]
                parsed.append([qualities[grade["등급"]], stat, lo, hi, float(option["획득 확률"].rstrip("%"))])
        type_data["slots"].append({"open": old["slots"][index]["open"], "options": parsed, "legacyOptionKeys": [[o[0], o[1]] for o in old["slots"][index]["options"]]})
        total += len(parsed)
    types.append(type_data)
assert len(types) == 5 and sum(len(t["slots"]) for t in types) == 45 and total == 2673
data = {
    "edition": "kr-official-20261012", "types": types, "percentageStats": sorted(percent_stats),
    "statNames": {"HardHit": "Golpe doble", "Perfect": "Golpe perfecto", "Restoration": "Regeneración", "PvEAddDamage": "Ataque JcE", "BossNpcAddDamage": "Ataque contra jefes"},
    "source": {"region": "KR", "label": "Versión coreana · referencia", "retrieved": "2026-10-12", "options": "https://probability.plaync.com/aion2/view?probCategoryId=68f1f99f57c8986afe4405d7", "grades": "https://probability.plaync.com/aion2/view?probCategoryId=68f1f99157c8986afe4405d5", "optionCount": total, "uniformValues": True, "experience": "DBAion2; referencia previa conservada, no publicada en las tablas de probabilidades", "percentageSimulationStep": "0.01%; resolución del simulador"},
}
(ROOT / "public/data/game/encyclopedia/race-understanding-kr.json").write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
print(json.dumps({"families": len(types), "slots": 45, "options": total, "percentageStats": len(percent_stats)}))
