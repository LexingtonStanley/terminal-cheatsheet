# cs import review report
_source: /home/bigballs/opencode-harness/regex-cheatsheet.md  ·  category: regex  ·  2026-06-08T03:25:55+01:00_

**imported:** 81  ·  **auto-corrected:** 9  ·  **flagged needs_review:** 13  ·  **skipped(dup/invalid):** 0

## auto-corrections & flags

- `grep "go*ld" file.txt` → corrected description (go*ld)
- `grep "colou+r" file.txt` → corrected description (colou+r), flagged BRE/ERE misuse
- `grep "colo?r" file.txt` → corrected description (colo?r), flagged BRE/ERE misuse
- `grep "error{2}" file.txt` → corrected description (error{2}), flagged BRE/ERE misuse
- `grep "error{2,}" file.txt` → corrected description (error{2,}), flagged BRE/ERE misuse
- `grep "error{2,5}" file.txt` → corrected description (error{2,5}), flagged BRE/ERE misuse
- `grep "a{3}" file.txt` → flagged BRE/ERE misuse
- `grep "[a-zA-Z0-9._%+-]" file.txt` → flagged BRE/ERE misuse
- `grep "(?:error|warn)" file.txt` → corrected description (error|warn), flagged BRE/ERE misuse
- `grep "error|warn" file.txt` → corrected description (error|warn), flagged BRE/ERE misuse
- `grep "foo|bar|baz" file.txt` → flagged BRE/ERE misuse
- `grep -P "password(?=)" file.txt` → added portability note, added portability note
- `grep -P "(?<=user=)password" file.txt` → added portability note, added portability note
- `grep -P "password(?!123)" file.txt` → added portability note, added portability note
- `grep -oE "(error|warn):.*" file.txt | cut -d: -f2` → corrected description (error|warn)
