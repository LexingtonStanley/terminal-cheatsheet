#!/usr/bin/env python3
"""cs — personal, append-only, fuzzy-searchable command cheatsheet.

Two consumers:
  * humans  -> `cs [query]` : fuzzy search (fzf) + rich preview (bat), copy/insert.
  * agents  -> `cs add ...` : structured, validated, append-only. Never rm/edit.

The store is JSONL, one file per category, under a git repo (the repo IS the
store). `cs` itself NEVER touches the network — it appends + makes a local
commit. Syncing to remotes is a separate concern (`cs-sync`, run by a timer).

Stdlib only. Shells out to fzf / bat / a clipboard tool for the UI.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

# ── paths ────────────────────────────────────────────────────────────────
# Resolve through symlinks: cs is symlinked into ~/.local/bin but lives in the
# repo, and the store (data/) sits next to it inside that same repo.
REPO = Path(os.environ.get("CS_HOME") or Path(__file__).resolve().parent)
DATA_DIR = Path(os.environ.get("CS_DATA_DIR") or (REPO / "data"))
TRASH = DATA_DIR / ".trash.jsonl"
CONFIG_DIR = Path(os.environ.get("XDG_CONFIG_HOME") or (Path.home() / ".config")) / "cs"
CONFIG_FILE = CONFIG_DIR / "config.toml"

REQUIRED_FIELDS = ("command", "name", "description", "category", "tags")
CATEGORY_RE = re.compile(r"^[a-z0-9][a-z0-9_-]*$")

# ── small utils ──────────────────────────────────────────────────────────
def now_iso() -> str:
    return _dt.datetime.now().astimezone().replace(microsecond=0).isoformat()


def eprint(*a, **k) -> None:
    print(*a, file=sys.stderr, **k)


def die(msg: str, code: int = 1):
    eprint(f"cs: {msg}")
    raise SystemExit(code)


def gen_id(command: str, category: str) -> str:
    h = hashlib.sha1(f"{command}\0{category}".encode()).hexdigest()
    return h[:12]


def have(binary: str) -> bool:
    return shutil.which(binary) is not None


# ── config (optional, per-machine, never synced) ─────────────────────────
def load_config() -> dict:
    cfg: dict = {}
    if CONFIG_FILE.exists():
        try:
            import tomllib

            cfg = tomllib.loads(CONFIG_FILE.read_text(encoding="utf-8"))
        except Exception as e:  # noqa: BLE001
            eprint(f"cs: warning: bad config {CONFIG_FILE}: {e}")
    return cfg


def ensure_config() -> None:
    if CONFIG_FILE.exists():
        return
    CONFIG_DIR.mkdir(parents=True, exist_ok=True)
    CONFIG_FILE.write_text(
        "# cs config (per-machine, NOT synced)\n"
        '# clipboard = "xclip"   # override autodetect: xclip|xsel|wl-copy|pbcopy|clip.exe\n'
        '# bat_theme = "ansi"    # bat preview theme\n'
        "# fzf_opts  = \"\"        # extra fzf flags\n"
        "auto_commit = true       # local git commit after add/import/rm\n",
        encoding="utf-8",
    )


# ── store io ─────────────────────────────────────────────────────────────
def category_file(category: str) -> Path:
    return DATA_DIR / f"{category}.jsonl"


def iter_entries(category: str | None = None):
    """Yield (entry, source_path) for every live entry."""
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    files = (
        [category_file(category)]
        if category
        else sorted(p for p in DATA_DIR.glob("*.jsonl") if not p.name.startswith("."))
    )
    for fp in files:
        if not fp.exists():
            continue
        for ln, line in enumerate(fp.read_text(encoding="utf-8").splitlines(), 1):
            line = line.strip()
            if not line:
                continue
            try:
                yield json.loads(line), fp
            except json.JSONDecodeError as e:
                eprint(f"cs: warning: {fp.name}:{ln}: bad JSON ({e}) — skipped")


def all_entries(category: str | None = None) -> list[dict]:
    return [e for e, _ in iter_entries(category)]


def find_entry(ident: str) -> tuple[dict, Path] | tuple[None, None]:
    for e, fp in iter_entries():
        if e.get("id") == ident:
            return e, fp
    return None, None


def append_entry(entry: dict) -> None:
    fp = category_file(entry["category"])
    fp.parent.mkdir(parents=True, exist_ok=True)
    # atomic-ish append: open in append mode, one write, fsync.
    with open(fp, "a", encoding="utf-8") as f:
        f.write(json.dumps(entry, ensure_ascii=False) + "\n")
        f.flush()
        os.fsync(f.fileno())


def rewrite_category(category: str, entries: list[dict]) -> None:
    """Rewrite a whole category file atomically (temp + rename)."""
    fp = category_file(category)
    tmp = fp.with_suffix(".jsonl.tmp")
    with open(tmp, "w", encoding="utf-8") as f:
        for e in entries:
            f.write(json.dumps(e, ensure_ascii=False) + "\n")
        f.flush()
        os.fsync(f.fileno())
    tmp.replace(fp)


# ── local git autocommit (NO network — that's cs-sync's job) ──────────────
def git_autocommit(message: str) -> None:
    cfg = load_config()
    if cfg.get("auto_commit") is False:
        return
    if not (REPO / ".git").exists() or not have("git"):
        return
    try:
        subprocess.run(["git", "-C", str(REPO), "add", "data"], check=False,
                       capture_output=True)
        r = subprocess.run(["git", "-C", str(REPO), "diff", "--cached", "--quiet"])
        if r.returncode != 0:  # there are staged changes
            subprocess.run(["git", "-C", str(REPO), "commit", "-q", "-m", message],
                           check=False, capture_output=True)
    except Exception:  # noqa: BLE001 — autocommit must never break a write
        pass


# ── validation ───────────────────────────────────────────────────────────
def validate_entry(e: dict) -> list[str]:
    errs = []
    for f in REQUIRED_FIELDS:
        if not e.get(f):
            errs.append(f"missing required field: {f}")
    cat = e.get("category", "")
    if cat and not CATEGORY_RE.match(cat):
        errs.append(f"bad category '{cat}' (use [a-z0-9_-])")
    if "tags" in e and not isinstance(e["tags"], list):
        errs.append("tags must be a list")
    if "examples" in e and not isinstance(e["examples"], list):
        errs.append("examples must be a list")
    return errs


def normalize_entry(e: dict) -> dict:
    """Fill defaults / coerce shapes so an entry is well-formed."""
    e.setdefault("tags", [])
    if isinstance(e["tags"], str):
        e["tags"] = [t.strip() for t in e["tags"].split(",") if t.strip()]
    e.setdefault("examples", [])
    e.setdefault("related", [])
    e.setdefault("needs_review", False)
    e.setdefault("added", now_iso())
    e["id"] = gen_id(e.get("command", ""), e.get("category", ""))
    # drop empties for tidiness
    for k in ("explanation", "gotchas", "portability", "source"):
        if k in e and not e[k]:
            del e[k]
    return e


# ── add ──────────────────────────────────────────────────────────────────
def parse_example(s: str) -> dict:
    """'cmd ::: note' -> {'cmd':..., 'note':...}; plain str -> {'cmd':...}."""
    if ":::" in s:
        cmd, note = s.split(":::", 1)
        return {"cmd": cmd.strip(), "note": note.strip()}
    return {"cmd": s.strip()}


def entry_from_args(a) -> dict:
    e = {
        "command": a.command,
        "name": a.name,
        "description": a.desc,
        "category": a.category,
        "tags": [t.strip() for t in (a.tags or "").split(",") if t.strip()],
    }
    if a.explanation:
        e["explanation"] = a.explanation
    if a.example:
        e["examples"] = [parse_example(x) for x in a.example]
    if a.gotcha:
        e["gotchas"] = a.gotcha if len(a.gotcha) > 1 else a.gotcha[0]
    if a.portability:
        e["portability"] = a.portability
    if a.source:
        e["source"] = a.source
    if a.related:
        e["related"] = [r.strip() for r in a.related.split(",") if r.strip()]
    return e


def add_one(e: dict, on_dup: str, reviewed: bool, dry_run: bool) -> tuple[str, str]:
    """Returns (status, id). status in added|skipped|updated|invalid."""
    errs = validate_entry(e)
    if errs:
        return ("invalid: " + "; ".join(errs), "")
    e = normalize_entry(e)
    # agent/owner adds are provisional until approved via `cs check`
    e["needs_review"] = not reviewed
    eid = e["id"]
    existing, fp = find_entry(eid)
    if existing:
        if on_dup == "skip":
            return ("skipped", eid)
        if on_dup in ("update", "merge"):
            if dry_run:
                return (on_dup + " (dry-run)", eid)
            merged = {**existing, **e} if on_dup == "merge" else e
            merged["id"] = eid
            cat = merged["category"]
            entries = [x for x in all_entries(cat) if x.get("id") != eid] + [merged]
            rewrite_category(cat, entries)
            return ("updated", eid)
    if dry_run:
        return ("would-add", eid)
    append_entry(e)
    return ("added", eid)


def cmd_add(a) -> int:
    # gather entries from stdin / --json / flags
    entries: list[dict] = []
    if a.stdin or a.json:
        raw = a.json if a.json else sys.stdin.read()
        try:
            obj = json.loads(raw)
        except json.JSONDecodeError as e:
            die(f"invalid JSON: {e}", 2)
        entries = obj if isinstance(obj, list) else [obj]
    else:
        if not a.command:
            die("nothing to add (use flags, --json, or --stdin)", 2)
        entries = [entry_from_args(a)]

    rc = 0
    added = 0
    for e in entries:
        status, eid = add_one(e, a.on_dup, a.reviewed, a.dry_run)
        tag = " [needs_review]" if (eid and not a.reviewed and status in
                                    ("added", "would-add")) else ""
        if status.startswith("invalid"):
            eprint(f"cs add: {status}")
            rc = 2
        elif status == "skipped":
            eprint(f"cs add: duplicate, skipped: {eid}")
        else:
            print(f"{status}: {eid}{tag}")
            if status in ("added", "updated"):
                added += 1
    if added and not a.dry_run:
        git_autocommit(f"cs add: {added} entr{'y' if added==1 else 'ies'}")
    return rc


# ── list / stats ─────────────────────────────────────────────────────────
def cmd_list(a) -> int:
    rows = all_entries(a.category)
    rows.sort(key=lambda e: (e.get("category", ""), e.get("name", "")))
    for e in rows:
        flag = " *" if e.get("needs_review") else ""
        print(f"{e.get('id'):12}  {e.get('category',''):10}  {e.get('name','')}{flag}")
    return 0


def cmd_stats(a) -> int:
    rows = all_entries()
    from collections import Counter

    cats = Counter(e.get("category", "?") for e in rows)
    tags = Counter(t for e in rows for t in e.get("tags", []))
    nrev = sum(1 for e in rows if e.get("needs_review"))
    print(f"entries: {len(rows)}   needs_review: {nrev}   categories: {len(cats)}")
    print("\nby category:")
    for c, n in cats.most_common():
        print(f"  {c:14} {n}")
    print("\ntop tags:")
    for t, n in tags.most_common(15):
        print(f"  {t:14} {n}")
    return 0


# ── render (entry -> markdown) ───────────────────────────────────────────
def entry_md(e: dict) -> str:
    out = [f"# {e.get('name','(unnamed)')}", ""]
    if e.get("needs_review"):
        out += ["> ⚠ **needs review** — not yet approved", ""]
    out += [f"`{e.get('command','')}`", "", e.get("description", ""), ""]
    meta = f"**category:** {e.get('category','')}"
    if e.get("tags"):
        meta += "   **tags:** " + ", ".join(e["tags"])
    out += [meta, ""]
    if e.get("explanation"):
        out += ["## explanation", "", e["explanation"], ""]
    if e.get("examples"):
        out += ["## examples", ""]
        for ex in e["examples"]:
            cmd = ex.get("cmd", "") if isinstance(ex, dict) else str(ex)
            note = ex.get("note", "") if isinstance(ex, dict) else ""
            out.append(f"- `{cmd}`" + (f" — {note}" if note else ""))
        out.append("")
    g = e.get("gotchas")
    if g:
        out += ["## gotchas", ""]
        out += [f"- {x}" for x in (g if isinstance(g, list) else [g])] + [""]
    if e.get("portability"):
        out += ["## portability", "", e["portability"], ""]
    if e.get("related"):
        out += ["## related", "", ", ".join(e["related"]), ""]
    if e.get("source"):
        out += [f"_source: {e['source']} · added {e.get('added','')}_", ""]
    return "\n".join(out)


def cmd_render(a) -> int:
    if a.all:
        rows = all_entries()
    else:
        rows = all_entries(a.category)
    rows.sort(key=lambda e: (e.get("category", ""), e.get("name", "")))
    parts = []
    cur = None
    for e in rows:
        c = e.get("category", "")
        if c != cur:
            parts.append(f"\n\n{'='*60}\n# CATEGORY: {c}\n{'='*60}\n")
            cur = c
        parts.append(entry_md(e))
        parts.append("\n---\n")
    md = "\n".join(parts)
    if a.out:
        Path(a.out).write_text(md, encoding="utf-8")
        print(f"wrote {a.out} ({len(rows)} entries)")
    else:
        sys.stdout.write(md)
    return 0


# ── preview (used by fzf) ────────────────────────────────────────────────
def cmd_preview(a) -> int:
    e, _ = find_entry(a.id)
    if not e:
        print("(entry not found)")
        return 1
    md = entry_md(e)
    cfg = load_config()
    bat = "batcat" if have("batcat") else ("bat" if have("bat") else None)
    if bat:
        theme = cfg.get("bat_theme", "ansi")
        p = subprocess.run([bat, "-l", "md", "--color=always", "-pp",
                            f"--theme={theme}"], input=md, text=True)
        return p.returncode
    print(md)
    return 0


# ── clipboard ────────────────────────────────────────────────────────────
def clipboard_cmd() -> list[str] | None:
    cfg = load_config()
    override = cfg.get("clipboard")
    table = {
        "wl-copy": ["wl-copy"],
        "xclip": ["xclip", "-selection", "clipboard"],
        "xsel": ["xsel", "-ib"],
        "pbcopy": ["pbcopy"],
        "clip.exe": ["clip.exe"],
    }
    if override and have(override.split()[0]):
        return table.get(override, override.split())
    for name, cmd in table.items():
        if have(name):
            return cmd
    return None


def copy_to_clipboard(text: str) -> bool:
    cmd = clipboard_cmd()
    if not cmd:
        return False
    try:
        subprocess.run(cmd, input=text, text=True, check=True)
        return True
    except Exception:  # noqa: BLE001
        return False


# ── search (fzf) ─────────────────────────────────────────────────────────
def entry_line(e: dict) -> str:
    """One `id \\t shown` row for the fzf list (used by stdin AND _lines reload)."""
    flag = "⚠ " if e.get("needs_review") else ""
    # id \t shown(category │ name ⟶ command)
    shown = f"{flag}{e.get('category',''):>8} │ {e.get('name','')}  ⟶  {e.get('command','')}"
    return f"{e['id']}\t{shown}"


def build_lines(rows: list[dict]) -> str:
    return "\n".join(entry_line(e) for e in rows)


def store_categories() -> list[str]:
    """Sorted list of category names that currently have entries."""
    return sorted({e.get("category", "") for e in all_entries() if e.get("category")})


def fzf_pick(rows: list[dict], query: str | None, category: str | None = None) -> dict | None:
    if not rows:
        eprint("cs: no entries yet — add some with `cs add` or `cs import`")
        return None
    if not have("fzf"):
        die("fzf not found — install fzf for interactive search", 3)
    self_exe = json_q(str(Path(__file__).resolve()))
    prompt = f"cs[{category}] ❯ " if category else "cs ❯ "
    cfg = load_config()
    fzf = [
        "fzf", "--ansi", "--delimiter", "\t", "--with-nth", "2..",
        "--height=90%", "--layout=reverse", "--border=rounded", "--info=inline",
        "--prompt", prompt, "--pointer", "▶",
        "--preview", f"{self_exe} _preview {{1}}",
        "--preview-window", "right:62%:wrap",
        "--header", "enter: copy · alt-c: cycle category · alt-a: all · ctrl-/: preview",
        "--bind", "ctrl-/:toggle-preview",
        # category cycler: _cycle reads $FZF_PROMPT and emits reload+change-prompt;
        # alt-a jumps straight back to the unfiltered list.
        "--bind", f"alt-c:transform:{self_exe} _cycle",
        "--bind", f"alt-a:reload({self_exe} _lines)+change-prompt(cs ❯ )",
    ]
    if query:
        fzf += ["--query", query]
    if cfg.get("fzf_opts"):
        fzf += cfg["fzf_opts"].split()
    env = dict(os.environ)
    p = subprocess.run(fzf, input=build_lines(rows), text=True, capture_output=True,
                       env=env)
    if p.returncode != 0 or not p.stdout.strip():
        return None
    sel_id = p.stdout.strip().split("\t", 1)[0]
    e, _ = find_entry(sel_id)
    return e


# ── hidden helpers backing the in-fzf category cycler ────────────────────
def cmd_lines(a) -> int:
    """Print the fzf list for all entries (or one --category). Used by reload()."""
    sys.stdout.write(build_lines(all_entries(a.category)))
    return 0


def cmd_cycle(a) -> int:
    """Emit an fzf action string advancing the picker to the next category.

    Stateless: the *current* category is recovered from $FZF_PROMPT (set by
    fzf for transform actions), so no temp files or shared state survive across
    reloads. Order is *all* → each category (sorted) → wrap.
    """
    order = ["*all*"] + store_categories()
    prompt = os.environ.get("FZF_PROMPT", "")
    m = re.search(r"cs\[([^\]]+)\]", prompt)
    cur = m.group(1) if m else "*all*"
    idx = order.index(cur) if cur in order else 0
    nxt = order[(idx + 1) % len(order)]
    exe = json_q(str(Path(__file__).resolve()))
    if nxt == "*all*":
        sys.stdout.write(f"reload({exe} _lines)+change-prompt(cs ❯ )")
    else:
        sys.stdout.write(f"reload({exe} _lines --category {nxt})"
                         f"+change-prompt(cs[{nxt}] ❯ )")
    return 0


def json_q(s: str) -> str:
    return "'" + s.replace("'", "'\\''") + "'"


def cmd_search(a) -> int:
    # scope: --tag, or positional that matches a category, else free query
    category = None
    query = a.query
    if a.tag:
        rows = [e for e in all_entries() if a.tag in e.get("tags", [])]
        query = None
    elif query and category_file(query).exists():
        rows = all_entries(query)
        category, query = query, None
    else:
        rows = all_entries()
    e = fzf_pick(rows, query, category)
    if not e:
        return 130  # cancelled
    cmd = e.get("command", "")
    if a.print_only:
        sys.stdout.write(cmd)  # for the ble.sh insert-widget (no newline)
        return 0
    print(cmd)
    if copy_to_clipboard(cmd):
        eprint("✓ copied to clipboard")
    else:
        eprint("(no clipboard tool found; printed above)")
    return 0


# ── rm (soft delete) — agents must NEVER call this ───────────────────────
def cmd_rm(a) -> int:
    e, fp = find_entry(a.id)
    if not e:
        die(f"no entry with id {a.id}", 1)
    if not a.yes:
        eprint(f"about to remove: {e.get('name')}  ({e.get('command')})")
        if input("delete? [y/N] ").strip().lower() not in ("y", "yes"):
            eprint("aborted")
            return 1
    cat = e["category"]
    remaining = [x for x in all_entries(cat) if x.get("id") != a.id]
    rewrite_category(cat, remaining)
    with open(TRASH, "a", encoding="utf-8") as f:
        e["_deleted"] = now_iso()
        f.write(json.dumps(e, ensure_ascii=False) + "\n")
    git_autocommit(f"cs rm: {a.id}")
    print(f"removed {a.id} (recoverable in {TRASH.name})")
    return 0


# ── edit ─────────────────────────────────────────────────────────────────
def cmd_edit(a) -> int:
    e, fp = find_entry(a.id)
    if not e:
        # maybe it's a category
        if category_file(a.id).exists():
            fp = category_file(a.id)
        else:
            die(f"no entry/category {a.id}", 1)
    editor = os.environ.get("EDITOR", "vi")
    subprocess.run([editor, str(fp)])
    # re-validate the file
    bad = 0
    for ent, _ in iter_entries(fp.stem):
        if validate_entry(ent):
            bad += 1
    if bad:
        eprint(f"cs: warning: {bad} invalid entr{'y' if bad==1 else 'ies'} after edit — run `cs check`")
    git_autocommit(f"cs edit: {fp.stem}")
    return 0


# ── §7 regex validation pass (seed data is untrusted AI output) ──────────
# Operators that are LITERAL in BRE (plain grep) but operators in ERE (-E).
# '*' and '.' are operators in BRE too, so they're excluded.
_ERE_ONLY = "+?{}|()"
# Known-wrong descriptions from the seed files -> corrected behaviour.
CORRECTIONS = {
    "go*ld": "matches 'g' + zero-or-more 'o' + 'ld' — i.e. 'gld','gold','goold'… "
             "(NOT 'g'/'go'/'goo'; the trailing 'ld' is required)",
    "colo?r": "ERE only: matches 'colr' or 'color' (optional 'o'). Does NOT match "
              "'colour' — that needs 'colou?r'.",
    "colou+r": "ERE only: matches 'colour','colouur'… (one-or-more 'u'). Does NOT "
               "match 'color'.",
    "error{2}": "ERE only: 'erro' then exactly two 'r' = 'errorr' (quantifier binds "
                "to the preceding 'r', not the whole word).",
    "error{2,}": "ERE only: 'erro' then two-or-more 'r' = 'errorr','errorrr'… "
                 "(this is 2-OR-MORE, not 'exactly 2').",
    "error{2,5}": "ERE only: 'erro' then 2 to 5 'r' (binds to preceding 'r').",
    "error|warn": "ERE only: matches 'error' or 'warn'. In plain grep '|' is literal "
                  "— use grep -E or escape as \\|.",
}


def first_quoted(s: str) -> str | None:
    m = re.search(r'"([^"]*)"|\'([^\']*)\'', s)
    if not m:
        return None
    return m.group(1) if m.group(1) is not None else m.group(2)


def grep_mode(command: str) -> str | None:
    """Return 'BRE' | 'ERE' | 'PCRE' | None (not a grep command)."""
    toks = command.split()
    if not toks:
        return None
    base = toks[0]
    if base == "egrep":
        return "ERE"
    if base != "grep":
        return None
    flags = " ".join(toks[1:])
    if re.search(r"(^|\s)-\w*P|--perl-regexp", flags):
        return "PCRE"
    if re.search(r"(^|\s)-\w*E|--extended-regexp", flags):
        return "ERE"
    return "BRE"


def regex_lint(command: str) -> list[str]:
    """Return a list of issue strings for a grep command (empty = clean)."""
    issues = []
    mode = grep_mode(command)
    if mode is None:
        return issues
    pat = first_quoted(command) or ""
    if mode == "BRE":
        # unescaped ERE-only operators in plain grep -> they're LITERAL here
        stripped = re.sub(r"\\.", "", pat)  # drop escaped pairs
        bad = sorted({c for c in stripped if c in _ERE_ONLY})
        if bad:
            issues.append(
                f"BRE/ERE: in plain grep the metacharacter(s) {' '.join(bad)} are "
                f"LITERAL, not operators. Use `grep -E` (or escape them) if you "
                f"meant them as operators.")
    if mode == "PCRE":
        issues.append("portability: `grep -P` (PCRE) is GNU-only — absent on "
                      "BSD/macOS grep.")
    if re.search(r"\(\?<?[=!]", pat):
        issues.append("portability: lookaround requires `grep -P` (GNU-only).")
    if re.search(r"[*+?]\?", pat):
        issues.append("portability: lazy/non-greedy quantifiers require `grep -P`.")
    return issues


def apply_validation(e: dict) -> tuple[dict, list[str]]:
    """Validate/annotate one entry. Returns (entry, list-of-notes-applied)."""
    notes = []
    cmd = e.get("command", "")
    # 1. known-wrong description corrections
    for key, fix in CORRECTIONS.items():
        if key in cmd:
            old = e.get("description", "")
            if old != fix:
                e["description"] = fix
                e["needs_review"] = True
                notes.append(f"corrected description ({key})")
            break
    # 2. regex/portability lint -> gotchas + needs_review
    for issue in regex_lint(cmd):
        g = e.get("gotchas")
        glist = g if isinstance(g, list) else ([g] if g else [])
        if issue not in glist:
            glist.append(issue)
        e["gotchas"] = glist
        e["needs_review"] = True
        if issue.startswith("BRE/ERE"):
            notes.append("flagged BRE/ERE misuse")
        elif "lookaround" in issue or "PCRE" in issue or "GNU-only" in issue:
            notes.append("added portability note")
    return e, notes


# ── markdown seed parser ─────────────────────────────────────────────────
_CMD_HEADS = ("grep", "egrep", "find", "tail", "sed", "awk", "echo", "rsync",
              "ssh", "scp", "cut", "sort", "uniq", "xargs", "inotifywait", "wc")


def parse_markdown(text: str, category: str, source: str) -> list[dict]:
    """Extract `cmd  # comment` lines (fenced or bare) into entries."""
    entries = []
    section = ""
    seen = set()
    for raw in text.splitlines():
        line = raw.rstrip()
        h = re.match(r"^#{1,6}\s+(.*)", line)
        if h:
            section = re.sub(r"^[\d.]+\s*", "", h.group(1)).strip()
            continue
        if line.strip().startswith("```") or line.strip().startswith("|"):
            continue
        body = line.strip()
        if not body or not body.split()[0].split("(")[0] in _CMD_HEADS:
            # also allow indented code lines whose first token is a command
            if not (body[:1] and body.split() and body.split()[0] in _CMD_HEADS):
                continue
        # split command / trailing comment
        m = re.match(r"^(.*?)\s+#\s*(.+)$", body)
        if m:
            command, comment = m.group(1).strip(), m.group(2).strip()
        else:
            command, comment = body, section or "command"
        if command.split()[0] not in _CMD_HEADS:
            continue
        if command in seen:
            continue
        seen.add(command)
        name = comment[:60]
        tags = sorted({command.split()[0]} | {category} |
                      {f for f in re.findall(r"(?<!\w)-(\w)", command)})
        entries.append({
            "command": command,
            "name": name,
            "description": comment,
            "category": category,
            "tags": [t for t in tags if t],
            "explanation": f"From '{section}'." if section else "",
            "source": source,
        })
    return entries


def cmd_import(a) -> int:
    fp = Path(a.file)
    if not fp.exists():
        die(f"no such file: {fp}", 1)
    text = fp.read_text(encoding="utf-8")
    category = a.category or fp.stem
    src = f"import:{fp.name}"
    # markdown vs json/jsonl
    if fp.suffix in (".json", ".jsonl"):
        raw = [json.loads(x) for x in text.splitlines() if x.strip()] \
            if fp.suffix == ".jsonl" else json.loads(text)
        cands = raw if isinstance(raw, list) else [raw]
        for c in cands:
            c.setdefault("category", category)
            c.setdefault("source", src)
    else:
        cands = parse_markdown(text, category, src)

    report = ["# cs import review report",
              f"_source: {fp}  ·  category: {category}  ·  {now_iso()}_", ""]
    added = corrected = flagged = skipped = 0
    review_rows = []
    for c in cands:
        c = normalize_entry(c)
        c["needs_review"] = True  # all seed imports are provisional
        c, notes = apply_validation(c)
        errs = validate_entry(c)
        if errs:
            report.append(f"- ⚠ SKIPPED (invalid): `{c.get('command','')}` — "
                          f"{'; '.join(errs)}")
            skipped += 1
            continue
        existing, _ = find_entry(c["id"])
        if existing and not a.force:
            skipped += 1
            continue
        if not a.dry_run:
            if existing:
                ents = [x for x in all_entries(c["category"])
                        if x.get("id") != c["id"]] + [c]
                rewrite_category(c["category"], ents)
            else:
                append_entry(c)
        added += 1
        if any("corrected" in n for n in notes):
            corrected += 1
        if any("flagged" in n or "portability" in n for n in notes):
            flagged += 1
        if notes:
            review_rows.append(f"- `{c['command']}` → {', '.join(notes)}")

    report += [f"**imported:** {added}  ·  **auto-corrected:** {corrected}  ·  "
               f"**flagged needs_review:** {flagged}  ·  **skipped(dup/invalid):** "
               f"{skipped}", ""]
    if review_rows:
        report += ["## auto-corrections & flags", ""] + review_rows + [""]
    rpt_text = "\n".join(report)
    if a.report:
        Path(a.report).write_text(rpt_text, encoding="utf-8")
        print(f"wrote review report: {a.report}")
    print(f"imported {added} ({corrected} corrected, {flagged} flagged, "
          f"{skipped} skipped)")
    if not a.report:
        print("\n" + rpt_text)
    if added and not a.dry_run:
        git_autocommit(f"cs import: {fp.name} (+{added}, {flagged} flagged)")
    return 0


# ── dedup (after a union merge, identical appended lines can duplicate) ──
def cmd_dedup(a) -> int:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    total = 0
    for fp in sorted(DATA_DIR.glob("*.jsonl")):
        if fp.name.startswith("."):
            continue
        by_id: dict[str, dict] = {}
        n = 0
        for e, _ in iter_entries(fp.stem):
            n += 1
            eid = e.get("id") or gen_id(e.get("command", ""), e.get("category", ""))
            # prefer the approved copy if a dup disagrees on needs_review
            if eid in by_id and by_id[eid].get("needs_review") and \
               not e.get("needs_review"):
                by_id[eid] = e
            else:
                by_id.setdefault(eid, e)
        if len(by_id) != n:
            rewrite_category(fp.stem, list(by_id.values()))
            total += n - len(by_id)
    print(f"dedup: removed {total} duplicate line(s)")
    return 0


# ── check / lint ─────────────────────────────────────────────────────────
def fzf_approve(review: list[dict]) -> list[str]:
    """fzf multi-select picker for clearing needs_review entries — same look as
    `cs` search, with the preview pane. Tab marks entries, ctrl-a marks all,
    ctrl-d clears marks, Enter approves the marked set (or the highlighted row if
    none are marked), Esc cancels. Returns the selected ids ([] on cancel)."""
    self_exe = json_q(str(Path(__file__).resolve()))
    fzf = [
        "fzf", "--ansi", "--multi", "--delimiter", "\t", "--with-nth", "2..",
        "--height=90%", "--layout=reverse", "--border=rounded", "--info=inline",
        "--prompt", "approve ❯ ", "--pointer", "▶", "--marker", "✓ ",
        "--preview", f"{self_exe} _preview {{1}}",
        "--preview-window", "right:62%:wrap",
        "--header", ("tab: mark · ctrl-a: all · ctrl-d: none · "
                     "enter: approve marked (else highlighted) · esc: cancel"),
        "--bind", "ctrl-/:toggle-preview",
        "--bind", "ctrl-a:select-all",
        "--bind", "ctrl-d:deselect-all",
    ]
    cfg = load_config()
    if cfg.get("fzf_opts"):
        fzf += cfg["fzf_opts"].split()
    p = subprocess.run(fzf, input=build_lines(review), text=True,
                       capture_output=True)
    if p.returncode not in (0, 1) or not p.stdout.strip():
        return []
    return [ln.split("\t", 1)[0] for ln in p.stdout.strip().splitlines() if ln.strip()]


def _commit_approvals(approved: list[dict]) -> int:
    """Clear needs_review on the given entries, persist per-category, commit."""
    changed_cats = {e["category"] for e in approved}
    for e in approved:
        e["needs_review"] = False
    for cat in changed_cats:
        by_id = {x["id"]: x for x in all_entries(cat)}
        for e in approved:
            if e["category"] == cat:
                by_id[e["id"]] = e
        rewrite_category(cat, list(by_id.values()))
    if changed_cats:
        git_autocommit("cs check: approved entries")
    n = len(approved)
    print(f"approved {n} entr{'y' if n == 1 else 'ies'} ✓" if n else "no entries approved.")
    return n


def cmd_check(a) -> int:
    rows = all_entries()
    invalid = [(e, validate_entry(e)) for e in rows]
    invalid = [(e, errs) for e, errs in invalid if errs]
    review = [e for e in rows if e.get("needs_review")]
    print(f"store: {len(rows)} entries · {len(invalid)} invalid · "
          f"{len(review)} need review")
    for e, errs in invalid:
        print(f"  ✗ {e.get('id')} {e.get('command','')}: {'; '.join(errs)}")
    if a.lint_only or not review:
        if not review and not a.lint_only:
            print("nothing needs review ✓")
        return 1 if invalid else 0

    # Default approval path = fzf multi-select picker (same UX as `cs` search),
    # whenever fzf is present and we're attached to a real terminal. No --approve
    # flag needed — `cs check` just opens the picker.
    if have("fzf") and sys.stdin.isatty() and sys.stdout.isatty():
        ids = set(fzf_approve(review))
        _commit_approvals([e for e in review if e["id"] in ids])
        return 0

    # Fallbacks for no-fzf / non-tty (e.g. piped). `--approve` runs the simple
    # text loop; otherwise just list what's pending.
    if not a.approve:
        print("\nentries needing review (run `cs check` in a terminal for the "
              "fzf picker, or `cs check --approve` for a text prompt):")
        for e in review:
            print(f"  ⚠ {e.get('id')}  {e.get('command','')}")
        return 0
    approved = []
    for e in review:
        print("\n" + entry_md(e))
        try:
            ans = input("[a]pprove / [s]kip / [q]uit > ").strip().lower()
        except EOFError:
            break
        if ans in ("q", "quit"):
            break
        if ans in ("a", "approve", "y", "yes"):
            approved.append(e)
    _commit_approvals(approved)
    return 0


# ── sync (delegates to cs-sync; the only networked path) ─────────────────
def cmd_sync(a) -> int:
    script = REPO / "cs-sync"
    if not script.exists():
        die("cs-sync not found next to cs", 1)
    return subprocess.run(["bash", str(script)], env={**os.environ,
                          "CS_HOME": str(REPO)}).returncode


# ── argparse ─────────────────────────────────────────────────────────────
def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="cs", description="personal command cheatsheet")
    sub = p.add_subparsers(dest="cmd")

    a = sub.add_parser("add", help="append a validated entry (agent contract)")
    a.add_argument("--command"); a.add_argument("--name"); a.add_argument("--desc")
    a.add_argument("--category"); a.add_argument("--tags")
    a.add_argument("--example", action="append", help="'cmd ::: note' (repeatable)")
    a.add_argument("--explanation"); a.add_argument("--gotcha", action="append")
    a.add_argument("--portability"); a.add_argument("--source"); a.add_argument("--related")
    a.add_argument("--on-dup", choices=["skip", "update", "merge"], default="skip",
                   dest="on_dup")
    a.add_argument("--json", help="a JSON object/array literal")
    a.add_argument("--stdin", action="store_true", help="read JSON object/array from stdin")
    a.add_argument("--reviewed", action="store_true",
                   help="mark approved (skip needs_review)")
    a.add_argument("--dry-run", action="store_true", dest="dry_run")
    a.set_defaults(func=cmd_add)

    ls = sub.add_parser("list", help="list ids + titles")
    ls.add_argument("category", nargs="?"); ls.set_defaults(func=cmd_list)

    st = sub.add_parser("stats", help="counts per category/tag")
    st.set_defaults(func=cmd_stats)

    rn = sub.add_parser("render", help="export polished markdown")
    rn.add_argument("category", nargs="?"); rn.add_argument("--all", action="store_true")
    rn.add_argument("--out"); rn.set_defaults(func=cmd_render)

    pv = sub.add_parser("_preview", help=argparse.SUPPRESS)
    pv.add_argument("id"); pv.set_defaults(func=cmd_preview)

    dd = sub.add_parser("_dedup", help=argparse.SUPPRESS)
    dd.set_defaults(func=cmd_dedup)

    lf = sub.add_parser("_lines", help=argparse.SUPPRESS)
    lf.add_argument("--category"); lf.set_defaults(func=cmd_lines)

    cy = sub.add_parser("_cycle", help=argparse.SUPPRESS)
    cy.set_defaults(func=cmd_cycle)

    rm = sub.add_parser("rm", help="soft-delete an entry (humans only)")
    rm.add_argument("id"); rm.add_argument("-y", "--yes", action="store_true")
    rm.set_defaults(func=cmd_rm)

    ed = sub.add_parser("edit", help="open entry/category in $EDITOR")
    ed.add_argument("id"); ed.set_defaults(func=cmd_edit)

    im = sub.add_parser("import", help="bulk import a md/json/jsonl file (validated)")
    im.add_argument("file")
    im.add_argument("--category", help="override category (default: file stem)")
    im.add_argument("--report", help="write the review report to this path")
    im.add_argument("--force", action="store_true", help="overwrite duplicates")
    im.add_argument("--dry-run", action="store_true", dest="dry_run")
    im.set_defaults(func=cmd_import)

    ck = sub.add_parser("check", help="validate store; approve provisional entries (fzf picker)")
    ck.add_argument("--approve", action="store_true",
                    help="text-prompt approval (used only when no fzf / not a tty)")
    ck.add_argument("--lint-only", action="store_true", dest="lint_only")
    ck.set_defaults(func=cmd_check)
    ln = sub.add_parser("lint", help="alias for `cs check --lint-only`")
    ln.set_defaults(func=cmd_check, approve=False, lint_only=True)

    sy = sub.add_parser("sync", help="reconcile store with remotes (runs cs-sync)")
    sy.set_defaults(func=cmd_sync)

    # search (default) — also reachable as `cs search`
    for name in ("search",):
        se = sub.add_parser(name, help="fuzzy search")
        se.add_argument("query", nargs="?")
        se.add_argument("--tag")
        se.add_argument("--print", action="store_true", dest="print_only",
                        help="print selected command to stdout only (for shell widget)")
        se.set_defaults(func=cmd_search)
    return p


def main(argv: list[str]) -> int:
    # Windows consoles default to a legacy code page (e.g. cp1252); force UTF-8 on
    # our streams so unicode in entries/preview prints instead of crashing. The
    # data files are read/written with explicit encoding="utf-8" everywhere.
    if sys.platform == "win32":
        for _s in (sys.stdout, sys.stderr):
            try: _s.reconfigure(encoding="utf-8")
            except Exception: pass
    ensure_config()
    parser = build_parser()
    # subcommand names are whatever the parser knows — derived, not hand-listed,
    # so new subcommands (incl. hidden _ones) never get misrouted to search.
    sub_action = next((a for a in parser._actions
                       if isinstance(a, argparse._SubParsersAction)), None)
    known = set(sub_action.choices) if sub_action else set()
    known |= {"-h", "--help"}
    # bare `cs` or `cs <query>`/`cs --tag ...` -> search
    if not argv or (argv[0] not in known and not argv[0].startswith("-")) or \
       (argv and argv[0] in ("--tag", "--print")):
        argv = ["search"] + argv
    a = parser.parse_args(argv)
    if not getattr(a, "func", None):
        parser.print_help()
        return 0
    return a.func(a)


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
