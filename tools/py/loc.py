"""Inserts localization lines into both hjson files.

add(anchor, {"en": [lines], "fr": [lines]}) inserts the lines right after the first line that starts
(after stripping tabs) with `anchor`, using that line's indentation.
"""
from ed import ROOT

FILES = {"en": "Localization/en-US_Mods.UltimateRandomizer.hjson", "fr": "Localization/fr-FR_Mods.UltimateRandomizer.hjson"}


def add(anchor, entries, block_end=False):
    for lang, lines in entries.items():
        path = ROOT + FILES[lang]
        with open(path, encoding="utf-8", newline="") as f:
            text = f.read()
        crlf = "\r\n" in text
        rows = text.replace("\r\n", "\n").split("\n")
        idx = next(i for i, r in enumerate(rows) if r.lstrip("\t").startswith(anchor))
        indent = rows[idx][: len(rows[idx]) - len(rows[idx].lstrip("\t"))]
        insert_at = idx + 1
        if block_end:
            # anchor opens a "Key: {" block: insert before its closing brace
            depth = 0
            for j in range(idx, len(rows)):
                depth += rows[j].count("{") - rows[j].count("}")
                if depth == 0:
                    insert_at = j
                    break
            indent += "\t"
        for k, line in enumerate(lines):
            rows.insert(insert_at + k, indent + line)
        out = "\n".join(rows)
        if crlf:
            out = out.replace("\n", "\r\n")
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(out)
    print("loc added after", anchor)
