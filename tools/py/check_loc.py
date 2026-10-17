import json,re,glob,sys,os,subprocess,tempfile
# Checks that every config field, enum value, header, test and used text key has an entry in both localization files.
# Usage: python tools/py/check_loc.py
ROOT=os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TML=os.environ.get("TML_DIR", r"D:\Jeux\Jeux Steam\steamapps\common\tModLoader")
S=tempfile.mkdtemp()
failed=False
for _lang in ("en-US","fr-FR"):
    _src=os.path.join(ROOT,"Localization",_lang+"_Mods.UltimateRandomizer.hjson")
    _dst=os.path.join(S,_lang+".json")
    subprocess.run(["pwsh","-NoProfile","-c",f"Add-Type -Path '{TML}\\Libraries\\hjson\\3.0.0\\lib\\netstandard1.3\\Hjson.dll'; [IO.File]::WriteAllText('{_dst}', [Hjson.HjsonValue]::Load('{_src}').ToString(), [Text.Encoding]::UTF8)"],check=True)
def flat(d,p=""):
    out=set()
    for k,v in d.items():
        if isinstance(v,dict): out|=flat(v,p+k+".")
        else: out.add(p+k)
    return out
src={}
for f in glob.glob(os.path.join(ROOT,"Common","**","*.cs"),recursive=True): src[f]=open(f,encoding="utf-8").read()
code="\n".join(src.values())
cfgsrc="\n".join(v for k,v in src.items() if os.sep+"Configs"+os.sep in k or "/Configs/" in k)
enums=dict(re.findall(r"public enum (\w+)\s*(?::\s*\w+\s*)?\{([^}]*)\}",code))
def enum_values(name):
    body=re.sub(r"//[^\n]*","",enums.get(name,""))
    return [v.split("=")[0].strip() for v in body.split(",") if v.split("=")[0].strip()]
# The mod's texts are loaded after Load (LanguageManager.ReloadLanguage at the SetupContent stage): a text read in a
# Load method, or by a UI state built there (OnInitialize), stays the raw key
for f,s in src.items():
    for body in re.findall(r"override void Load\(\)\s*\{(.*?)\n\t\t\}",s,re.S):
        if re.search(r"Language\.GetText|\.Activate\(\)",body):
            print("text read in Load (raw keys):",os.path.relpath(f,ROOT)); failed=True
for lang in ("en-US","fr-FR"):
    keys=flat(json.load(open(os.path.join(S,lang+".json"),encoding="utf-8-sig")))
    missing=[]
    for m in re.finditer(r"public (?:abstract )?class (\w+)([^{]*)\{(.*?)\n\t\}",cfgsrc,re.S):
        cls,head,body=m.group(1),m.group(2),m.group(3)
        if cls=="RandomizerConfig": continue
        if re.search(r"RandomizerConfig|ModConfig",head) and f"Configs.{cls}.DisplayName" not in keys: missing.append(f"{cls}.DisplayName")
        for h in re.findall(r'\[Header\("(\w+)"\)\]',body):
            if f"Configs.{cls}.Headers.{h}" not in keys: missing.append(f"{cls}.Headers.{h}")
        for typ,name in re.findall(r"public (\w+(?:<\w+>)?) (\w+)(?: = [^;]*)?;",body):
            if f"Configs.{cls}.{name}.Label" not in keys: missing.append(f"{cls}.{name}")
            if typ in enums:
                for val in re.findall(r"^\s*([A-Z]\w*)\s*,?\s*$",re.sub(r"//[^\n]*","",enums[typ]),re.M):
                    if f"Configs.{typ}.{val}.Label" not in keys: missing.append(f"enum {typ}.{val}")
    for k in set(re.findall(r'"Mods\.UltimateRandomizer\.([\w.]+?)"',code)):
        # Prefixes completed at run time ("Mods.UltimateRandomizer.Tests." + id) are not keys
        if not k.endswith(".") and k not in keys: missing.append("used key "+k)
    for a,b in set(re.findall(r'DebugTools\.Say\("(\w+)"|\bT\("(\w+)"',code)):
        kk=a or b
        if kk=="Kind": continue  # T("Kind" + kind): KindNormal, KindGiant, KindMini
        if "Debug."+kk not in keys: missing.append("Debug."+kk)
    for k in set(re.findall(r'TestRunner\.Register\("(\w+)"',code)):
        if "Tests."+k not in keys: missing.append("Tests."+k)
    # Codex texts: CodexText.Get/Live("Key") is "Codex.Key"; CodexText.Of("Group", value) is "Codex.Group." + an enum name
    for k in set(re.findall(r'CodexText\.(?:Get|Live)\("(\w[\w.]*)"(?!\s*\+)',code)):
        if "Codex."+k not in keys: missing.append("used key Codex."+k)
    for group,enum in (("Source","CodexSource"),("Category","ItemCategory")):
        for val in enum_values(enum):
            if f"Codex.{group}.{val}" not in keys: missing.append(f"Codex.{group}.{val} (enum {enum})")
    # Hint sentences (HintFacts): every variant of each family, the world sources, and the phrases built from names
    hints=src.get(os.path.join(ROOT,"Common","Codex","HintFacts.cs"),"")
    m=re.search(r"string\[\] HintTexts =\s*\{([^}]*)\}",hints)
    for fam,n in re.findall(r'"([\w.]+)/(\d+)"',m.group(1) if m else ""):
        for v in range(int(n)):
            if f"Codex.Hint.{fam}.{v}" not in keys: missing.append(f"Codex.Hint.{fam}.{v}")
    for key in re.findall(r'\(ItemID\.\w+, "(\w+)", ',hints):
        for part in ("Kind","Where","Shape","Name"):
            if f"Codex.Hint.World.{key}.{part}" not in keys: missing.append(f"Codex.Hint.World.{key}.{part}")
    for arr,group in (("StationGroups","Station"),("Movements","Move")):
        m=re.search(arr+r" = \{([^}]*)\}",hints)
        for val in re.findall(r'"(\w+)"',m.group(1) if m else ""):
            if f"Codex.Hint.{group}.{val}" not in keys: missing.append(f"Codex.Hint.{group}.{val}")
    for val in enum_values("SpawnBiome"):
        for part in ("Name","Description"):
            if f"Codex.Biome.{val}.{part}" not in keys: missing.append(f"Codex.Biome.{val}.{part} (enum SpawnBiome)")
    for val in enum_values("SlotTime"):
        if f"Codex.Hint.Time.{val}" not in keys: missing.append(f"Codex.Hint.Time.{val} (enum SlotTime)")
    for k in ["Codex.Hint.Band."+str(b) for b in range(4)]+["Codex.Hint.Coin."+c for c in ("Copper","Silver","Gold","Platinum")]:
        if k not in keys: missing.append(k)
    # Tab buttons: "Tab" + each CodexPageKind of CodexUIState.Tabs; debug tabs: "Debug." + each name of its tabs array
    m=re.search(r"CodexPageKind\[\] Tabs = \{([^}]*)\}",code)
    for val in re.findall(r"CodexPageKind\.(\w+)",m.group(1) if m else ""):
        if "Codex.Tab"+val not in keys: missing.append("Codex.Tab"+val)
    m=re.search(r'string\[\] tabs = \{([^}]*)\}',src.get(os.path.join(ROOT,"Common","Debug","DebugUIState.cs"),""))
    for val in re.findall(r'"(\w+)"',m.group(1) if m else ""):
        if "Debug."+val not in keys: missing.append("Debug."+val)
    for k in ("Tests.NoWorld","Debug.SpawnRateNormal","Debug.SpawnRateNone","Debug.SpawnRateHigh","Keybinds.Debug.DisplayName","Keybinds.Journal.DisplayName"):
        if k not in keys: missing.append(k)
    print(lang, "missing:", sorted(set(missing)) or "none")
    if missing: failed=True

sys.exit(1 if failed else 0)
