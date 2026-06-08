# `cs` — agent integration contract

`cs` is a personal command cheatsheet on this machine. When we establish a
useful command, flag, or pattern during a session, **persist it** so it
survives at zero token cost.

## The rule

- Persist knowledge **only** via `cs add` (it appends + auto-dedups).
- **Never** hand-edit `data/*.jsonl`. **Never** call `cs rm` or `cs edit`.
- Your entries are written immediately but flagged `needs_review=true` until
  the owner approves them with `cs check`. So: add freely; don't worry about
  being wrong — provisional entries are visibly provisional.
- One entry per distinct command. Re-running the same `cs add` is a safe no-op.

## Single add

```sh
cs add --command '<canonical command with <placeholders>>' \
       --name '<short label>' \
       --desc '<one line: what it does>' \
       --category '<grep|regex|files|transfer|process|net|...>' \
       --tags '<comma,separated>' \
       --example '<literal invocation> ::: <optional note>' \
       [--explanation '<longer teaching note>'] \
       [--gotcha '<pitfall/warning>'] \
       [--portability '<GNU vs BSD/macOS notes>'] \
       --source 'agent-session <date>'
```

`--example` and `--gotcha` are repeatable. Placeholders go in `<angle_brackets>`.

## Batch add (whole session at once)

Pipe a JSON **array** to `cs add --stdin`. Each object's schema:

```json
{
  "command":     "rsync -avz <src> <dst>",        // REQUIRED
  "name":        "rsync copy between machines",     // REQUIRED
  "description": "mirror a dir, preserving perms",  // REQUIRED
  "category":    "transfer",                        // REQUIRED ([a-z0-9_-])
  "tags":        ["rsync","transfer","ssh"],        // REQUIRED (list)
  "explanation": "…",                               // optional
  "examples":    [{"cmd":"rsync -avz a/ b/","note":"trailing slash matters"}],
  "gotchas":     ["trailing slash on src changes behaviour"],
  "portability": "-z (compress) everywhere; --info=progress2 is GNU rsync 3.1+",
  "related":     ["<other entry id>"],
  "source":      "agent-session 2026-06-08"
}
```

```sh
echo '[ {…}, {…} ]' | cs add --stdin
```

## Safety

`cs` performs **only local file appends** to its own data dir, plus a local
git commit — **no network calls, ever**. (Syncing to other machines is a
separate `cs-sync` step run on a timer, not by `cs add`.) Safe to call from
any agent shell session; `cs` is on `$PATH`.
