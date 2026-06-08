# cs import review report
_source: /home/bigballs/opencode-harness/opencode-harness.md  ·  category: grep  ·  2026-06-08T03:25:54+01:00_

**imported:** 87  ·  **auto-corrected:** 11  ·  **flagged needs_review:** 9  ·  **skipped(dup/invalid):** 0

## auto-corrections & flags

- `grep -E "error|warn" logfile.txt` → corrected description (error|warn)
- `grep "error{2,}" logfile.txt` → corrected description (error{2,}), flagged BRE/ERE misuse
- `grep "error{2,5}" logfile.txt` → corrected description (error{2,5}), flagged BRE/ERE misuse
- `grep -r "eval(" --include="*.js" .` → flagged BRE/ERE misuse
- `grep "colo?r" file.txt` → corrected description (colo?r), flagged BRE/ERE misuse
- `grep "go*ld" file.txt` → corrected description (go*ld)
- `grep "colou+r" file.txt` → corrected description (colou+r), flagged BRE/ERE misuse
- `grep "error{2}" file.txt` → corrected description (error{2}), flagged BRE/ERE misuse
- `grep "error{2,}" file.txt` → corrected description (error{2,}), flagged BRE/ERE misuse
- `grep "error{2,5}" file.txt` → corrected description (error{2,5}), flagged BRE/ERE misuse
- `grep "error|warn" file.txt` → corrected description (error|warn), flagged BRE/ERE misuse
- `grep -E "error|warn" file.txt` → corrected description (error|warn)
