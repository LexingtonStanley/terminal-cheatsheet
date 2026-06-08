#!/usr/bin/env bash
# install-harness.sh — wire `cs` into Claude Code + opencode on this machine.
#
# Idempotent: safe to re-run (it symlinks/merges, never duplicates). Run it once
# per machine after cloning cs. Picks up updates to the canonical files in this
# repo automatically (opencode tool + rules are symlinked, not copied).
#
#   ./integrations/harness/install-harness.sh            # both harnesses
#   ./integrations/harness/install-harness.sh --claude   # Claude Code only
#   ./integrations/harness/install-harness.sh --opencode # opencode only
set -euo pipefail

HERE="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"   # integrations/harness
REPO="$(cd "$HERE/../.." && pwd)"                          # cs repo root
DO_CLAUDE=1; DO_OPENCODE=1
case "${1:-}" in
  --claude)   DO_OPENCODE=0 ;;
  --opencode) DO_CLAUDE=0 ;;
  "" )        ;;
  * ) echo "usage: $0 [--claude|--opencode]"; exit 2 ;;
esac
say() { printf '  %s\n' "$*"; }

# ── opencode ──────────────────────────────────────────────────────────────
if [ "$DO_OPENCODE" = 1 ]; then
  echo "opencode:"
  OC="${XDG_CONFIG_HOME:-$HOME/.config}/opencode"
  mkdir -p "$OC/tool"
  ln -sfn "$HERE/opencode-cs.ts" "$OC/tool/cs.ts";  say "tool/cs.ts -> repo (cs_add, cs_list)"
  ln -sfn "$HERE/cs-rules.md"    "$OC/cs-rules.md"; say "cs-rules.md -> repo"
  RULES="$OC/cs-rules.md" python3 - "$OC/opencode.json" <<'PY'
import json, os, sys
p = sys.argv[1]
cfg = {}
if os.path.exists(p):
    try: cfg = json.load(open(p))
    except Exception: cfg = {}
cfg.setdefault("$schema", "https://opencode.ai/config.json")
ins = cfg.get("instructions") or []
rules = os.environ["RULES"]
if rules not in ins:
    ins.append(rules); cfg["instructions"] = ins
    if os.path.exists(p): os.replace(p, p + ".bak")
    json.dump(cfg, open(p, "w"), indent=2); open(p, "a").write("\n")
    print("  opencode.json: instructions -> cs-rules.md")
else:
    print("  opencode.json: already references cs-rules.md")
PY
fi

# ── Claude Code ───────────────────────────────────────────────────────────
if [ "$DO_CLAUDE" = 1 ]; then
  echo "Claude Code:"
  CC="$HOME/.claude"; mkdir -p "$CC"
  # 1) global CLAUDE.md — insert/replace the cs-harness block between markers
  SNIP="$HERE/claude-CLAUDE.md" python3 - "$CC/CLAUDE.md" <<'PY'
import os, re, sys
p = sys.argv[1]
snip = open(os.environ["SNIP"]).read().strip()
cur = open(p).read() if os.path.exists(p) else ""
beg, end = "<!-- cs-harness:begin", "cs-harness:end -->"
if beg in cur and end in cur:
    cur = re.sub(re.escape(beg) + r".*?" + re.escape(end), snip, cur, flags=re.S)
    msg = "CLAUDE.md: updated cs-harness block"
else:
    head = "" if cur else "# Global user instructions (all projects on this machine)\n\n"
    cur = (cur.rstrip() + "\n\n" if cur else "") + head + snip + "\n"
    msg = "CLAUDE.md: added cs-harness block"
open(p, "w").write(cur); print("  " + msg)
PY
  # 2) settings.json — merge permissions.allow (dedup), keep everything else
  PERMS="$HERE/claude-permissions.json" python3 - "$CC/settings.json" <<'PY'
import json, os, sys
p = sys.argv[1]
want = json.load(open(os.environ["PERMS"]))["permissions"]["allow"]
cfg = {}
if os.path.exists(p):
    try: cfg = json.load(open(p))
    except Exception: cfg = {}
perms = cfg.setdefault("permissions", {})
allow = perms.setdefault("allow", [])
added = [r for r in want if r not in allow]
if added:
    allow.extend(added)
    if os.path.exists(p): os.replace(p, p + ".bak")
    json.dump(cfg, open(p, "w"), indent=2); open(p, "a").write("\n")
    print("  settings.json: +%d cs allow rule(s)" % len(added))
else:
    print("  settings.json: cs allow rules already present")
PY
fi

echo "done. opencode picks up changes next launch; Claude Code in new sessions."
