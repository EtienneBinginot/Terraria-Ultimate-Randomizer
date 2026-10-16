"""Edits the Configs section of both hjson files: config fields, headers, enum values, whole classes."""
import json
from ed import ROOT
from loc import FILES


def q(text):
    return json.dumps(text, ensure_ascii=False)


class Loc:
    def __init__(self, lang):
        self.path = ROOT + FILES[lang]
        with open(self.path, encoding="utf-8", newline="") as f:
            text = f.read()
        self.crlf = "\r\n" in text
        self.rows = text.replace("\r\n", "\n").split("\n")

    def save(self):
        out = "\n".join(self.rows)
        if self.crlf:
            out = out.replace("\n", "\r\n")
        with open(self.path, "w", encoding="utf-8", newline="") as f:
            f.write(out)

    def find(self, prefix, start=0, end=None):
        end = len(self.rows) if end is None else end
        for i in range(start, end):
            if self.rows[i] == prefix or self.rows[i].startswith(prefix + " ") or self.rows[i] == prefix.rstrip():
                return i
        return -1

    def block_end(self, start):
        depth = 0
        for j in range(start, len(self.rows)):
            depth += self.rows[j].count("{") - self.rows[j].count("}")
            if depth == 0:
                return j
        raise ValueError("unbalanced block")

    def class_span(self, cls):
        configs = self.find("Configs: {")
        start = self.find("\t" + cls + ": {", configs)
        if start < 0:
            raise KeyError(cls)
        return start, self.block_end(start)

    def has(self, cls, field):
        start, end = self.class_span(cls)
        return self.find("\t\t" + field + ": {", start, end) >= 0


def _field_rows(name, label, tooltip):
    return ["", f"\t\t{name}: {{", f"\t\t\tLabel: {q(label)}", f"\t\t\tTooltip: {q(tooltip)}", "\t\t}"]


def field(cls, name, after, en, fr):
    """Adds a config field block (label, tooltip) after field `after` (None = at the end of the class)."""
    for lang, (label, tooltip) in (("en", en), ("fr", fr)):
        loc = Loc(lang)
        start, end = loc.class_span(cls)
        if loc.find("\t\t" + name + ": {", start, end) >= 0:
            continue
        if after is None:
            at = end
            rows = _field_rows(name, label, tooltip)
        else:
            a = loc.find("\t\t" + after + ": {", start, end)
            if a < 0:
                raise KeyError(f"{cls}.{after}")
            at = loc.block_end(a) + 1
            rows = _field_rows(name, label, tooltip)
        loc.rows[at:at] = rows
        loc.save()


def remove_field(cls, name):
    for lang in ("en", "fr"):
        loc = Loc(lang)
        start, end = loc.class_span(cls)
        a = loc.find("\t\t" + name + ": {", start, end)
        if a < 0:
            continue
        b = loc.block_end(a)
        if loc.rows[a - 1].strip() == "":
            a -= 1
        del loc.rows[a:b + 1]
        loc.save()


def label(cls, name, en, fr):
    """Replaces the label and tooltip of an existing field."""
    for lang, (lab, tip) in (("en", en), ("fr", fr)):
        loc = Loc(lang)
        start, end = loc.class_span(cls)
        a = loc.find("\t\t" + name + ": {", start, end)
        b = loc.block_end(a)
        loc.rows[a:b + 1] = _field_rows(name, lab, tip)[1:]
        loc.save()


def header(cls, key, en, fr):
    for lang, text in (("en", en), ("fr", fr)):
        loc = Loc(lang)
        start, end = loc.class_span(cls)
        h = loc.find("\t\tHeaders: {", start, end)
        if h < 0:
            loc.rows[start + 1:start + 1] = ["\t\tHeaders: {", "\t\t}", ""]
            h = start + 1
        hend = loc.block_end(h)
        if loc.find("\t\t\t" + key + ":", h, hend) >= 0:
            continue
        loc.rows.insert(hend, f"\t\t\t{key}: {q(text)}")
        loc.save()


def copy_class(src, dst, tooltip_en="", tooltip_fr=""):
    for lang, tip in (("en", tooltip_en), ("fr", tooltip_fr)):
        loc = Loc(lang)
        try:
            loc.class_span(dst)
            continue
        except KeyError:
            pass
        start, end = loc.class_span(src)
        block = loc.rows[start:end + 1]
        block[0] = "\t" + dst + ": {"
        for i, r in enumerate(block):
            if r.startswith("\t\tTooltip:"):
                block[i] = "\t\tTooltip: " + q(tip)
                break
        loc.rows[end + 1:end + 1] = [""] + block
        loc.save()


def new_class(cls, after_cls, tooltip_en, tooltip_fr):
    """Adds an empty class block (for a new nested config object or enum)."""
    for lang, tip in (("en", tooltip_en), ("fr", tooltip_fr)):
        loc = Loc(lang)
        try:
            loc.class_span(cls)
            continue
        except KeyError:
            pass
        _, end = loc.class_span(after_cls)
        loc.rows[end + 1:end + 1] = ["", "\t" + cls + ": {", "\t\tTooltip: " + q(tip), "\t}"]
        loc.save()


def enum_value(enum, value, en, fr):
    """Enum values use the same layout as fields: Enum: { Value: { Label, Tooltip } }."""
    for lang, (lab, tip) in (("en", en), ("fr", fr)):
        loc = Loc(lang)
        try:
            start, end = loc.class_span(enum)
        except KeyError:
            configs = loc.find("Configs: {")
            cend = loc.block_end(configs)
            loc.rows[cend:cend] = ["", "\t" + enum + ": {", "\t}"]
            start, end = loc.class_span(enum)
        if loc.find("\t\t" + value + ": {", start, end) >= 0:
            continue
        loc.rows[end:end] = _field_rows(value, lab, tip)
        loc.save()
