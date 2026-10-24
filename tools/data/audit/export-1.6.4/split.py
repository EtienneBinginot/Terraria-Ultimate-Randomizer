# Splits the Ultimate Randomizer world export into compact chunk files for the audit agents
import csv, os, re, sys
csv.field_size_limit(10**8)
EXP = "C:/Users/etien/Documents/My Games/Terraria/tModLoader/UltimateRandomizer/Export/"
OUT = os.path.dirname(os.path.abspath(__file__)) + "/chunks/"
os.makedirs(OUT, exist_ok=True)

def rows(name):
    with open(EXP + name, encoding="utf-8", newline="") as f:
        r = csv.reader(f, delimiter="\t", quoting=csv.QUOTE_NONE)
        head = next(r)
        for row in r:
            yield dict(zip(head, row))

def short(ref):
    # "Wood (Bois)" -> "Wood"
    return ref.split(" (")[0] if ref else ""

def ingr(s):
    # "Wood x10 [tier 0, avail 0, value 1]; ..." -> "Wood x10 t0/a0/v1; ..."
    return re.sub(r"\[tier (\S+), avail (\S+), value (\S+)\]", r"t\1/a\2/v\3", s or "").replace(" ;", ";")

def write(name, header, lines):
    with open(OUT + name, "w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(header) + "\n")
        for l in lines:
            f.write("\t".join(str(x) for x in l) + "\n")
    print(name, len(lines), os.path.getsize(OUT + name))

# Items reference (grep it)
items = {}
lines = []
for r in rows("items.tsv"):
    items[r["internal name"]] = r
    lines.append([r["id"], r["internal name"], r["name"], r["category"], r["tier"], r["true tier"], r["findability tier"], r["vanilla recipe tier"], r["planned recipe tier"], r["protected"], r["excluded"], r["banned"], r["mod"]])
write("items-ref.tsv", ["id", "internal", "name", "category", "tier", "true tier", "findability tier", "vanilla recipe tier", "planned recipe tier", "protected", "excluded", "banned", "mod"], lines)

# Recipes by result tier
rec = {}
for r in rows("recipes.tsv"):
    flags = [k for k in ("randomized", "rebuilt", "swapped", "shell", "upgrade shell", "protected", "house", "repaired", "value short") if r.get(k) == "True"]
    if r.get("switched off"): flags.append("off:" + r["switched off"])
    t = r["result item tier"]
    rec.setdefault(t, []).append([r["index"], r["mod"], short(r["result"]), r["stack"], t, ingr(r["vanilla ingredients"]), r["vanilla stations"], ingr(r["planned ingredients"]), r["planned stations"],
        r["key ingredient"], ",".join(flags), r["swap partner"], r["vanilla value"], r["planned value"], r["planned doable tier"], r["ingredient tier T"], r["lock key"]])
rh = ["index", "mod", "result", "stack", "result tier", "vanilla ingredients", "vanilla stations", "planned ingredients (item xN tTier/aAvail/vValue)", "planned stations", "key ingredient", "flags", "swap partner", "vanilla value", "planned value", "planned doable tier", "ingredient tier T", "lock key"]
t0 = rec.get("0", [])
q = (len(t0) + 3) // 4
for i in range(4):
    write(f"recipes-t0-{i+1}.tsv", rh, t0[i*q:(i+1)*q])
write("recipes-t1.tsv", rh, rec.get("1", []))
write("recipes-t2.tsv", rh, rec.get("2", []))
write("recipes-t3-5.tsv", rh, sum((rec.get(str(t), []) for t in (3, 4, 5)), []))
write("recipes-t6-9.tsv", rh, sum((rec.get(str(t), []) for t in (6, 7, 8, 9)), []) + sum((v for k, v in rec.items() if k not in [str(x) for x in range(10)]), []))

# Drops (normal mode; expert rows only where they differ)
dh = ["owner", "owner name", "owner tier", "boss", "spawn tier", "mode", "idx", "item", "item tier", "stack", "chance", "conditions", "kind", "original item", "origin npc"]
drops = {}
normal = {}
for r in rows("drops.tsv"):
    key = (r["owner"], r["idx"])
    sig = (r["item"], r["chance"], r["conditions"], r["stack min"], r["stack max"])
    if r["expert"] == "False":
        normal[key] = sig
    elif normal.get(key) == sig:
        continue
    t = r["owner tier"]
    drops.setdefault(t, []).append([r["owner"], r["owner name"], t, r["boss"], r["spawn tier"], "expert" if r["expert"] == "True" else "normal", r["idx"], short(r["item"]), r["item tier"],
        f'{r["stack min"]}-{r["stack max"]}', r["chance"], r["conditions"], r["kind"], r["original item"], r["origin npc"]])
for name, ts in (("drops-t0-1.tsv", "01"), ("drops-t2-3.tsv", "23"), ("drops-t4-5.tsv", "45"), ("drops-t6-9.tsv", "6789")):
    write(name, dh, sum((drops.get(t, []) for t in ts), []) + (sum((v for k, v in drops.items() if k not in "0123456789"), []) if ts == "6789" else []))

# Bags
lines = [[r["owner"], r["owner name"], r["owner tier"], r["boss"], r["idx"], short(r["item"]), r["item tier"], f'{r["stack min"]}-{r["stack max"]}', r["chance"], r["conditions"], r["kind"], r["original item"]] for r in rows("bags.tsv")]
write("bags.tsv", ["bag", "bag name", "bag tier", "boss bag", "idx", "item", "item tier", "stack", "chance", "conditions", "kind", "original item"], lines)

# Shops: tier 0 rows, tiers 4 and 8 rows, added slots of every tier (compact)
shh = ["npc", "shop", "tier", "slot", "original", "orig tier", "conditions", "slot kind", "replacement", "repl category", "repl tier", "repl true tier", "price", "vanilla price", "shop-only"]
sh = {"0": [], "48": [], "added": []}
for r in rows("shops.tsv"):
    line = [r["npc"].replace("Terraria/", ""), r["shop"].split("/")[-1], r["unlocked tier"], r["slot"], r["original"], r["original tier"], r["conditions"], r["slot kind"],
        r["replacement"], r["replacement category"], r["replacement tier"], r["replacement true tier"], r["price"], r["vanilla price"], r["shop-only"]]
    if r["slot kind"] == "added slot": sh["added"].append(line)
    elif r["unlocked tier"] == "0": sh["0"].append(line)
    elif r["unlocked tier"] in ("4", "8"): sh["48"].append(line)
write("shops-t0.tsv", shh, sh["0"])
write("shops-t4-t8.tsv", shh, sh["48"])
write("shops-added.tsv", shh, sh["added"])

# Findability: everything not obviously fine (never, or found 2+ tiers away from its tier), plus world sources and chests
fh = None
lines = []
for r in rows("findability.tsv"):
    if fh is None: fh = list(r.keys())
    try:
        tier, found = int(r["tier"]), r["findability tier"]
        ra = r["recipe availability tier"]
        never = found == "never"
        cls = "unobtainable" if never and ra in ("", "never") and not r["first shop tier"] else "far" if not never and abs(int(found) - tier) >= 2 else ""
    except ValueError:
        cls = "parse"
    if cls: lines.append([cls] + list(r.values()))
write("findability-odd.tsv", ["class"] + fh, lines)
with open(OUT + "worldsources-chests.txt", "w", encoding="utf-8") as f:
    f.write(open(EXP + "worldsources.tsv", encoding="utf-8").read() + "\n\n" + open(EXP + "chests.tsv", encoding="utf-8").read() + "\n\n" + open(EXP + "README.txt", encoding="utf-8").read())
