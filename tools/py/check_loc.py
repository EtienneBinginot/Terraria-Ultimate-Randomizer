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
enums=dict(re.findall(r"public enum (\w+)\s*\{([^}]*)\}",code))
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
    for k in ("Tests.NoWorld","Debug.SpawnRateNormal","Debug.SpawnRateNone","Debug.SpawnRateHigh","Keybinds.Debug.DisplayName","Keybinds.Journal.DisplayName"):
        if k not in keys: missing.append(k)
    print(lang, "missing:", sorted(set(missing)) or "none")
    if missing: failed=True

sys.exit(1 if failed else 0)
