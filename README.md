# cs — personal command cheatsheet

An append-only, fuzzy-searchable command knowledge base that lives in your
terminal and syncs across your machines. You learn a command during an agent
session, the agent persists it with `cs add`, and it's recallable forever at
**zero token cost** — `cs` is the durable memory.

Built for the WezTerm + Zellij + Yazi + ble.sh setup. Stdlib Python; shells
out to `fzf` (search) and `bat` (preview).

## Quick start

```sh
cs                       # fuzzy-search everything; Enter copies the command
cs grep                  # scope to a category
cs --tag ssh             # scope to a tag
cs <anything>            # pre-seed the search query
#   inside the picker:  Alt-c cycles category filter · Alt-a back to all
#                       Ctrl-/ toggles the preview

cs add --command 'grep -rn <pat> <dir>' --name 'recursive grep' \
       --desc 'search recursively with line numbers' \
       --category grep --tags grep,recursive \
       --example 'grep -rn TODO src/'

cs list [category]       # ids + titles  (* = needs review)
cs stats                 # counts per category / tag
cs render grep           # polished markdown for a category
cs render --all --out cheatsheet.md
cs check                 # review/approve provisional entries
cs import file.md        # bulk import (runs the validation pass)
cs rm <id>               # soft-delete (recoverable) — humans only
```

## How it fits together

- **Store:** `~/.local/share/cs/data/<category>.jsonl` — one JSON object per
  line, append-safe, git-friendly. The store dir is a git repo.
- **Agents** persist knowledge via `cs add` only — see [`AGENTS.md`](AGENTS.md).
  Their entries are flagged `needs_review` until you approve them with `cs check`.
- **Sync** is decoupled: `cs` never touches the network. `cs-sync` (a daily
  systemd timer + an on-login pull) reconciles with the remotes. JSONL appends
  merge line-by-line, so concurrent adds on different machines don't conflict.
- **Hotkeys:** `Alt-/` opens `cs` in a Zellij floating pane; `Alt-s` (ble.sh)
  fuzzy-picks an entry and types its command onto your current prompt line.

## Use it from your AI agents (Claude Code + opencode)

`cs` is wired into both harnesses so agents can persist commands **on request**
("remember this", "cs this") — never proactively. One command sets it up on a
new machine (idempotent, safe to re-run):

```sh
~/.local/share/cs/integrations/harness/install-harness.sh            # both
~/.local/share/cs/integrations/harness/install-harness.sh --claude   # one only
~/.local/share/cs/integrations/harness/install-harness.sh --opencode
```

What it wires (canonical files live in [`integrations/harness/`](integrations/harness/),
so they travel with this repo and update via `cs-sync`):

| Harness | What gets installed |
|---|---|
| **Claude Code** | A `cs` block in `~/.claude/CLAUDE.md` (global memory, on-request contract) + `permissions.allow` rules in `~/.claude/settings.json` so `cs add` and the read-only verbs never prompt. `cs rm`/`cs edit` are **not** allowed (still prompt — humans only). Claude Code has no user-defined native tools, so `cs` is invoked as the CLI via Bash. |
| **opencode** | Native tools **`cs_add`** + **`cs_list`** symlinked into `~/.config/opencode/tool/cs.ts` (opencode globs `{tool,tools}/*.{js,ts}`; tool id = `basename`+`_export`). The on-request rule (`cs-rules.md`) is symlinked in and referenced from `opencode.json` `"instructions"`. |

**Does the global `~/.claude/CLAUDE.md` clobber a per-agent / per-project
identity?** No. Claude Code memory is **layered and additive** — managed → user
(`~/.claude/CLAUDE.md`) → project (`./CLAUDE.md`) → local → subdir — all
concatenated, with more-specific files appended after (so a project can override
the global). The block installed here contains **only** the `cs` capability — no
persona, no identity claims — so it sits alongside your project/agent identity
without conflict. Subagents inherit user memory too, which is exactly what you
want (every agent can persist on request). To remove it, delete the block
between the `cs-harness:begin`/`end` markers.

> Manual install (if you'd rather not run the script): symlink
> `integrations/harness/opencode-cs.ts` → `~/.config/opencode/tool/cs.ts` and
> `integrations/harness/cs-rules.md` → `~/.config/opencode/cs-rules.md`, add that
> rules path to `opencode.json` `"instructions"`; for Claude Code, paste
> `integrations/harness/claude-CLAUDE.md` into `~/.claude/CLAUDE.md` and merge
> `claude-permissions.json` into `~/.claude/settings.json` `permissions.allow`.

## Sync remotes

- **Primary:** `lexde@lexbox:git/terminal-cheatsheet.git` (over Tailscale)
- **Mirror:** private GitHub `LexingtonStanley/terminal-cheatsheet`

## Config (per-machine, not synced)

`~/.config/cs/config.toml` — clipboard override, bat theme, extra fzf opts,
`auto_commit`. Auto-generated on first run.

## Tests

```sh
python3 tests/test_cs.py
```
