"""Generated blocks for README.src.md (called by libs-Tools/readme-forge/forge_readme.py).

The Ledger is read from the code (Configuration.cs: every config.Bind call and every configSync.AddConfigEntry), so
the store page always shows the defaults, ranges and sync side the DLL ships with.
"""
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))

# What each rune seals on this page (the rune legend's last column).
RUNE_SEALS = {
    "ᚱ": "the name: every road through the gates",
    "ᛞ": "what is new",
    "ᚾ": "what you need to know first",
    "ᚨ": "the destination window and the map",
    "ᛊ": "the photographs and the live view",
    "ᛉ": "locks and self-healing gates",
    "ᛖ": "tames and mounts",
    "ᛇ": "the corpse run",
    "ᛈ": "the concealed gates",
    "ᚲ": "the runic gateway",
    "ᛏ": "the server's law: limits, filters, health",
    "ᛃ": "the ledger of defaults",
    "ᚠ": "the toolbox",
    "ᚷ": "multiplayer",
    "ᛗ": "other mods",
    "ᛚ": "the settings",
    "ᛟ": "the credits",
}

# The dials shown outside the fold, in this order. Everything numeric is in the full table under it.
HIGHLIGHTS = [
    "MenuOpenDistance", "MaxPortalsPerPlayer", "PortalIndexRefreshSeconds", "SnapshotRefreshIntervalMinutes",
    "TameFollowRadius", "CorpseRunClearanceMeters", "CorpseRunSearchRadiusMeters", "VFXWakeDistance",
]


def _split_args(s, start):
    """Return (args, end) for the call whose '(' is at s[start]; string- and paren-aware."""
    depth, i, cur, args = 0, start, [], []
    in_str = False
    while i < len(s):
        ch = s[i]
        if in_str:
            cur.append(ch)
            if ch == "\\":
                cur.append(s[i + 1])
                i += 2
                continue
            if ch == '"':
                in_str = False
        elif ch == '"':
            in_str = True
            cur.append(ch)
        elif ch == "(":
            depth += 1
            if depth > 1:
                cur.append(ch)
        elif ch == ")":
            depth -= 1
            if depth == 0:
                args.append("".join(cur).strip())
                return args, i
            cur.append(ch)
        elif ch == "," and depth == 1:
            args.append("".join(cur).strip())
            cur = []
        else:
            cur.append(ch)
        i += 1
    raise SystemExit("readme_gen: unbalanced config.Bind call in Configuration.cs")


def _settings():
    with open(os.path.join(HERE, "Configuration.cs"), encoding="utf-8-sig") as fh:
        src = fh.read()
    synced = set(re.findall(r"configSync\.Add(?:Locking)?ConfigEntry\((\w+)\)", src))
    out = []
    for m in re.finditer(r"(\w+)\s*=\s*config\.Bind\(", src):
        args, _ = _split_args(src, m.end() - 1)
        if len(args) < 3:
            continue
        section, key, default = args[0].strip('"'), args[1].strip('"'), args[2]
        rest = ",".join(args[3:])
        rng = re.search(r"AcceptableValueRange<\w+>\(\s*([^,]+?)\s*,\s*([^)]+?)\s*\)", rest)
        out.append(dict(field=m.group(1), section=section, key=key, default=default,
                        lo=rng.group(1) if rng else None, hi=rng.group(2) if rng else None,
                        synced=m.group(1) in synced))
    if len(out) < 60:
        raise SystemExit(f"readme_gen: read only {len(out)} settings from Configuration.cs")
    return out


def _is_meta(s):
    # The file's layout stamp: bound with the MetaSection constant, not a player-facing setting.
    return s["section"] in ("MetaSection", "0 - Meta") or s["key"] == "ConfigVersionKey"


def _num(v):
    v = v.strip().rstrip("fFdDmM")
    try:
        f = float(v)
    except ValueError:
        return None
    return str(int(f)) if f == int(f) else f"{f:g}"


def _default(s):
    n = _num(s["default"])
    if n is not None:
        if s["key"] == "MaxPortalsPerPlayer" and n == "0":
            return "**no cap**"
        if s["key"] == "CorpseRunGateTtlMinutes" and n == "0":
            return "**no limit**"
        return f"**{n}**"
    d = s["default"]
    if d in ("true", "false"):
        return "on" if d == "true" else "off"
    return f"`{d.split('.')[-1]}`" if "." in d else f"`{d}`"


def _row(s):
    rng = f"{_num(s['lo'])} – {_num(s['hi'])}" if s["lo"] else "—"
    side = "🔒" if s["synced"] else "🖥️"
    return f"| `{s['key']}` | {_default(s)} | {rng} | {side} |"


def ledger(forge):
    settings = _settings()
    by_key = {s["key"]: s for s in settings}
    missing = [k for k in HIGHLIGHTS if k not in by_key]
    if missing:
        raise SystemExit(f"readme_gen: highlight settings not in the code: {missing}")
    head = "| Dial | Default | Range | Side |\n|---|---|---|---|"
    out = [head] + [_row(by_key[k]) for k in HIGHLIGHTS]

    numeric = [s for s in settings if _num(s["default"]) is not None and not _is_meta(s)]
    out += ["", "<details>", f"<summary>📜 <b>The whole ledger</b> — all {len(numeric)} numeric dials, by section</summary>", "", head]
    for s in sorted(numeric, key=lambda s: (s["section"], s["key"])):
        out.append(_row(s))
    out += ["", "</details>"]
    return "\n".join(out)


def counts(forge):
    settings = [s for s in _settings() if not _is_meta(s)]
    synced = sum(1 for s in settings if s["synced"])
    return f"{len(settings)} settings: {synced} 🔒 server-synced, {len(settings) - synced} 🖥️ local"
