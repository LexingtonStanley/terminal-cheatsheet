# cs — command cheatsheet (global rule)

This machine has `cs`, a personal append-only command cheatsheet
(`~/.local/share/cs`). It lets useful commands survive across sessions at zero
token cost. Two native tools are available: **`cs_add`** (persist a command)
and **`cs_list`** (read-only listing).

## When to use it — ON REQUEST ONLY

Call **`cs_add`** only when the operator **explicitly** asks to remember / save
/ persist a command — phrases like "remember this", "save that command",
"cs this", "add it to the cheatsheet". **Do not add commands proactively** and
do not add on your own initiative; capture is operator-driven.

When asked, write a clean entry: put variable parts in `<angle_brackets>`, give
a short `name`, a one-line `description`, a lowercase `category` (freeform —
e.g. `linux`, `nmap`, `net`, `recon`, `vim`, `python`), and comma-separated
`tags`. Add an `example` and a `gotcha` when they help.

Your entries are saved flagged `needs_review` until the operator approves them
with `cs check` — so don't worry about being wrong; provisional entries are
visibly provisional. Re-adding the same command is a safe no-op (deduped).

Use **`cs_list`** (optionally with a category) to see what's already stored
before adding, or to recall what exists.

## Never

- Never delete or edit entries (there is no tool for it; `cs rm` / `cs edit`
  are humans-only). `cs` is local-only — it appends + makes a local git commit,
  never any network call.
