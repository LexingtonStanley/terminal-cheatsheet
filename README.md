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
