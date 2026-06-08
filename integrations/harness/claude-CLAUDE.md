<!-- cs-harness:begin — managed by ~/.local/share/cs/integrations/harness; safe to leave as-is -->
## cs — command cheatsheet (persist learned commands ON REQUEST)

This machine has `cs`, a personal append-only command cheatsheet on `$PATH`
(`~/.local/share/cs`). It lets useful commands survive across sessions at zero
token cost. It is **local-only** — it appends + makes a local git commit, never
any network call. Safe to call from any agent shell.

**When to use it — ON REQUEST ONLY.** Run `cs add` only when I explicitly ask
to remember / save / persist a command — "remember this", "save that command",
"cs this", "add it to the cheatsheet". **Do not add proactively.**

When I ask, add a clean entry (placeholders in `<angle_brackets>`):

```sh
cs add --command '<cmd with <placeholders>>' --name '<short label>' \
       --desc '<one line>' --category '<lowercase: linux|nmap|net|vim|...>' \
       --tags 'a,b' --example '<invocation> ::: <note>' \
       --source 'agent-session'
```

Batch many at once by piping a JSON array to `cs add --stdin`. Categories are
freeform (any new `--category` auto-creates its store). Agent adds are written
immediately but flagged `needs_review=true` until I approve them with
`cs check --approve`; re-adding the same command is a safe no-op (deduped on
command+category).

**Never** call `cs rm` or `cs edit` (humans-only). Full contract + schema:
`~/.local/share/cs/AGENTS.md`. Recall what's stored with `cs list [category]`
or `cs stats`; interactive fuzzy search is just `cs` (Alt-c cycles category,
Enter copies).
<!-- cs-harness:end -->
