// cs.ts — native opencode tools backing the `cs` command cheatsheet.
//   tool ids: cs_add (persist a command), cs_list (read-only listing).
// `cs` is the personal, append-only command cheatsheet at ~/.local/share/cs.
// It never touches the network — it just appends + makes a local git commit.
// ON-REQUEST ONLY: only call cs_add when the operator explicitly asks to
// remember/save a command. Agent adds land as needs_review until approved.
//
// Install: symlink this to ~/.config/opencode/tool/cs.ts (opencode globs
// {tool,tools}/*.{js,ts} and follows symlinks). See ../../README.md and
// install-harness.sh in this directory.
import { tool } from "@opencode-ai/plugin"

const HOME = process.env.HOME || "/home/bigballs"
// resolve order: $CS_BIN override -> the ~/.local/bin symlink -> the repo copy
const CS = process.env.CS_BIN || `${HOME}/.local/bin/cs`

function runCs(args: string[]): { ok: boolean; out: string } {
  const p = Bun.spawnSync([CS, ...args], { stdout: "pipe", stderr: "pipe" })
  const out = ((p.stdout?.toString() ?? "") + (p.stderr?.toString() ?? "")).trim()
  return { ok: p.exitCode === 0, out }
}

export const add = tool({
  description:
    "Persist a useful shell command to the `cs` cheatsheet so it is recallable " +
    "forever at zero token cost. Use ONLY when the operator explicitly asks to " +
    "remember / save / 'cs this' a command — never proactively. Put variable " +
    "parts in <angle_brackets>. The entry is saved flagged needs_review until " +
    "the operator approves it with `cs check`. Re-adding the same command is a " +
    "safe no-op (deduped by command+category).",
  args: {
    command: tool.schema
      .string()
      .describe("Canonical command, placeholders in <angle_brackets>, e.g. 'nmap -sV <host>'"),
    name: tool.schema.string().describe("Short label, e.g. 'service/version scan'"),
    description: tool.schema.string().describe("One line: what it does"),
    category: tool.schema
      .string()
      .describe("Lowercase category [a-z0-9_-]; freeform, e.g. linux, nmap, net, recon, vim"),
    tags: tool.schema.string().describe("Comma-separated tags, e.g. 'nmap,recon,net'"),
    example: tool.schema
      .string()
      .optional()
      .describe("A concrete invocation; optional inline note as 'cmd ::: note'"),
    explanation: tool.schema.string().optional().describe("Longer teaching note (optional)"),
    gotcha: tool.schema.string().optional().describe("A pitfall / warning (optional)"),
    source: tool.schema.string().optional().describe("Where it came from (optional)"),
  },
  async execute(args) {
    const a = [
      "add",
      "--command", args.command,
      "--name", args.name,
      "--desc", args.description,
      "--category", args.category,
      "--tags", args.tags,
      "--source", args.source || "opencode-session",
      "--on-dup", "update",
    ]
    if (args.example) a.push("--example", args.example)
    if (args.explanation) a.push("--explanation", args.explanation)
    if (args.gotcha) a.push("--gotcha", args.gotcha)
    const r = runCs(a)
    return r.out || (r.ok ? "added" : "cs add failed")
  },
})

export const list = tool({
  description:
    "List entries already in the `cs` cheatsheet (ids + titles; '*' = needs_review). " +
    "Read-only. Pass a category to scope, omit it for everything. Use this to check " +
    "what is already stored before adding, or to recall what exists.",
  args: {
    category: tool.schema
      .string()
      .optional()
      .describe("Category to scope to, e.g. 'nmap' (omit for all categories)"),
  },
  async execute(args) {
    const a = ["list"]
    if (args.category) a.push(args.category)
    const r = runCs(a)
    return r.out || "(no entries)"
  },
})
