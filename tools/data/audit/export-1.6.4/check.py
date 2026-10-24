# Global coherence checks of an Ultimate Randomizer world export (the defect classes of the 1.6.4 audit)
import csv, re, sys
from collections import Counter, defaultdict
csv.field_size_limit(10**8)
EXP = "C:/Users/etien/Documents/My Games/Terraria/tModLoader/UltimateRandomizer/Export/"

def rows(name):
    with open(EXP + name, encoding="utf-8", newline="") as f:
        r = csv.reader(f, delimiter="\t", quoting=csv.QUOTE_NONE)
        head = next(r)
        for row in r:
            yield dict(zip(head, row))

def num(s, d=None):
    try: return int(s)
    except:
        try: return float(s.replace(",", "."))
        except: return d

items = {r["internal name"]: r for r in rows("items.tsv")}
def tier(name): return num(items.get(name, {}).get("true tier", ""), None)
def cat(name): return items.get(name, {}).get("category", "")
def find(name): return items.get(name, {}).get("findability tier", "")
def short(ref): return ref.split(" (")[0] if ref else ""
GEAR = {"WeaponMelee", "WeaponRanged", "WeaponMagic", "WeaponSummon", "WeaponOther", "Armor", "Accessory", "Tool", "MountPet"}
out = []
def section(title, lines, limit=12):
    out.append(f"\n## {title}: {len(lines)}")
    out.extend("  " + l for l in lines[:limit])

# ---- Recipes
ING = re.compile(r"(\S+) x(\d+) \[tier (\S+), avail (\S+), value (\S+)\]")
at30, lines_total, craftonly_big, never_ing, early, late_gear, empty_live, over_value, big_total = [], 0, [], [], [], [], [], [], []
locked, locked_keyed = 0, []
for r in rows("recipes.tsv"):
    if r["switched off"] or r["protected"] == "True": continue
    ings = ING.findall(r["planned ingredients"])
    res = short(r["result"]); rt = num(r["result item tier"], 0); doable = r["planned doable tier"]
    d = num(doable, None)
    if r["randomized"] == "True" and not ings and r["shell"] != "True": empty_live.append(f'{r["index"]} {res}')
    total = 0
    for name, n, t, a, v in ings:
        lines_total += 1; n = int(n); total += n
        if n >= 30: at30.append(f'{r["index"]} {res}: {name} x{n}')
        if a == "never": never_ing.append(f'{r["index"]} {res}: {name}')
        if find(name) == "never" and n > 5 and r["rebuilt"] == "True": craftonly_big.append(f'{r["index"]} {res}: {name} x{n}')
    if r["rebuilt"] == "True" and d is not None and d < rt and r["shell"] != "True": early.append(f'{r["index"]} {res} tier {rt} doable {d}')
    if cat(res) in GEAR and d is not None and d > (tier(res) or rt) + 1 and r["rebuilt"] == "True": late_gear.append(f'{r["index"]} {res} true {tier(res)} doable {d}')
    vv, pv = num(r["vanilla value"], 0), num(r["planned value"], 0)
    if r["rebuilt"] == "True" and r["swapped"] != "True" and vv and pv > vv * 2 + 100 and r["upgrade shell"] != "True": over_value.append(f'{r["index"]} {res}: {vv} -> {pv}')
    # Station locks (1.8.0+ exports): no key, the planned station is had exactly at T
    if r.get("station lock") == "True":
        locked += 1
        if r["key ingredient"]: locked_keyed.append(f'{r["index"]} {res}: key {r["key ingredient"]}')
    if r["rebuilt"] == "True" and total > 40: big_total.append(f'{r["index"]} {res}: {total} items ({r["planned ingredients"][:120]})')
section(f"Recipe ingredient lines at >=30 (of {lines_total})", at30)
section("Recipe totals above 40 items", big_total)
section("Craft-only ingredients asked > 5", craftonly_big)
section("Ingredients never available in live recipes", never_ing)
section("Rebuilt recipes doable before their result tier", early)
section("Gear craftable 2+ tiers after its true tier", late_gear)
section("Live randomized recipes without ingredients", empty_live)
section("Recipes above 2x vanilla value", over_value)
section(f"Station-locked recipes (of {locked}) that also have a key", locked_keyed)

# ---- Drops and bags
def drops_checks(name, label):
    far, pot, ammo, single, weap100, dup = [], [], [], [], [], Counter()
    for r in rows(name):
        if r["expert"] == "True": continue
        it = short(r["item"]); ot = num(r["owner tier"], 0); ch = num(r["chance"], 0) or 0
        smax = num(r["stack max"], 1) or 1; c = cat(it); t = tier(it)
        dup[(r["owner"], it, r["conditions"])] += 1
        if r["kind"] in ("extra", "surprise", "permuted") and t is not None and t > ot + 2 and ch >= 0.02 and num(r["jackpot gap"], 0) == 0:
            far.append(f'{r["owner"]} (t{ot}): {it} t{t} {ch:.3f} {r["kind"]}')
        if r["kind"] in ("extra", "surprise") and c in ("Potion", "Food") and smax > 5: pot.append(f'{r["owner"]}: {it} x{smax} {ch:.2f}')
        if r["kind"] in ("extra", "surprise") and c == "Ammo" and smax > 50: ammo.append(f'{r["owner"]}: {it} x{smax} {ch:.2f}')
        if it in ("TempleKey", "LihzahrdPowerCell", "ShadowKey", "GoldenKey", "CelestialSigil") and smax > 1: single.append(f'{r["owner"]}: {it} x{smax}')
        if r["boss"] == "False" and c in GEAR and ch >= 0.99 and r["kind"] != "kept": weap100.append(f'{r["owner"]}: {it} {r["kind"]}')
    section(f"{label}: items 3+ tiers above owner at >=2 %", far)
    section(f"{label}: potion/food extras above 5", pot)
    section(f"{label}: ammo extras above 50", ammo)
    section(f"{label}: single-use items stacked", single)
    section(f"{label}: gear at 100 % on non-boss owners", weap100)
    section(f"{label}: same item 3+ times on one owner (same conditions)", [f"{k[0]}: {k[1]} x{v}" for k, v in dup.items() if v >= 3])
drops_checks("drops.tsv", "Drops")
drops_checks("bags.tsv", "Bags")

# ---- Shops
spread, price_bad, ahead = defaultdict(set), [], []
for r in rows("shops.tsv"):
    if r["slot kind"] == "unchanged": continue
    t = num(r["unlocked tier"], 0); rep = r["replacement"]; p = num(r["price"], 0)
    spread[(t, rep)].add(r["npc"])
    rt = num(r["replacement true tier"], 0)
    if r["replacement category"] in GEAR and rt > t + 1 and r["slot kind"] == "roll": ahead.append(f't{t} {r["npc"]}: {rep} true {rt}')
    if p > (t + 1) * 1_000_000: price_bad.append(f't{t} {r["npc"]}: {rep} {p}')
section("Shops: item in more than 4 merchants at one tier", [f"t{k[0]} {k[1]}: {len(v)}" for k, v in spread.items() if len(v) > 4])
section("Shops: gear rolled 2+ tiers ahead", ahead)
section("Shops: price above (tier+1) x 100 gold", price_bad)

# ---- Findability of must-haves
must = ["SuspiciousLookingEye", "WormFood", "BloodySpine", "SlimeCrown", "Abeemination", "DeerThing", "GuideVoodooDoll", "MechanicalEye", "MechanicalWorm",
        "MechanicalSkull", "QueenSlimeCrystal", "TruffleWorm", "LihzahrdPowerCell", "CelestialSigil", "Pwnhammer", "TempleKey", "ShadowKey", "GoldenKey",
        "WorkBench", "Furnace", "IronAnvil", "LeadAnvil", "Hellforge", "MythrilAnvil", "OrichalcumAnvil", "AdamantiteForge", "TitaniumForge", "TinkerersWorkbench",
        "LunarCraftingStation", "SoulofLight", "SoulofNight", "SoulofFlight", "HallowedBar", "ChlorophyteBar", "Ectoplasm", "BeetleHusk", "LunarOre",
        "JungleKey", "CrimsonKey", "CorruptionKey", "HallowedKey", "FrozenKey", "DungeonDesertKey", "MagicMirror", "IceMirror"]
fl = []
for r in rows("findability.tsv"):
    if r["internal name"] in must:
        f, ra, t = r["findability tier"], r["recipe availability tier"], num(r["tier"], 0)
        best = min([num(x, 99) for x in (f, ra) if x not in ("", "never")] or [99])
        if best > t + 1: fl.append(f'{r["internal name"]}: tier {t}, found {f}, recipe {ra or "-"}')
section("Must-have items obtained 2+ tiers late (or never)", fl, 40)
print("\n".join(out))
