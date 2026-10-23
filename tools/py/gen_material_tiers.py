import json
p='tools/data/material-tiers.json'
d=json.load(open(p,encoding='utf-8'))
fix={}  # one-off user corrections already applied: CrimtaneOre, DemoniteOre -> 1
for x in d:
    if x['k'] in fix:
        x['t']=fix[x['k']]; x['no']+=' (user: not minable before a pickaxe tier after the Eye; tier 1)'
json.dump(d,open(p,'w',encoding='utf-8',newline='\n'),ensure_ascii=False,indent=0)
cm={'common':'Common','uncommon':'Uncommon','rare':'Rare'}
L=[]
L.append('using System.Collections.Generic;\nusing Terraria.ID;\n\nnamespace UltimateRandomizer.Common.Core\n{\n')
L.append('\t// How often a material should come back as a random recipe ingredient\n\tpublic enum Commonness { Common, Uncommon, Rare }\n\n')
L.append('\t// Hand-reviewed vanilla materials (tools/data/material-tiers.json, generated): the tier they are really obtained at\n')
L.append('\t// (null: banned, never obtainable) and their commonness. Generated, do not edit by hand: change the JSON instead.\n')
L.append('\tpublic static class MaterialTiers\n\t{\n\t\tpublic static readonly Dictionary<int, (int? Tier, Commonness Commonness)> Table = new()\n\t\t{\n')
for x in sorted(d,key=lambda x:x['k']):
    t='null' if x['t']=='ban' else str(x['t'])
    L.append(f"\t\t\t[ItemID.{x['k']}] = ({t}, Commonness.{cm[x['cm']]}),\n")
L.append('\t\t};\n\t}\n}\n')
open('Common/Core/MaterialTiers.cs','w',encoding='utf-8',newline='\r\n').write(''.join(L))
