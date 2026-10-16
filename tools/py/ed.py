import os

# Repository root (this file is tools/py/ed.py)
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))).replace(os.sep, "/") + "/"


class Patch:
    def __init__(self, rel):
        self.path = ROOT + rel
        with open(self.path, encoding="utf-8", newline="") as f:
            self.s = f.read()
        self.crlf = "\r\n" in self.s
        self.s = self.s.replace("\r\n", "\n")

    def rep(self, a, b, count=1):
        n = self.s.count(a)
        assert n == count, f"{self.path}: expected {count} match(es), found {n}: {a[:80]!r}"
        self.s = self.s.replace(a, b)
        return self

    def save(self):
        out = self.s.replace("\n", "\r\n") if self.crlf else self.s
        with open(self.path, "w", encoding="utf-8", newline="") as f:
            f.write(out)
        print("patched", os.path.relpath(self.path, ROOT))
