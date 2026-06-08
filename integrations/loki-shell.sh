# cs integrations for bash/ble.sh — source this from your shell rc.
# (In the Loki setup this block is merged into ~/.loki-term/shell/loki-shell.sh.)

# ── Alt-s : fuzzy-pick a saved command and INSERT it on the prompt line ───
# Works in both plain readline and ble.sh (ble.sh emulates READLINE_LINE/POINT
# for `bind -x`). fzf manages its own tty, so this is safe inside a widget.
if command -v cs >/dev/null 2>&1; then
  __cs_insert() {
    local c
    c=$(cs --print) || return
    [ -n "$c" ] || return
    READLINE_LINE="${READLINE_LINE:0:READLINE_POINT}${c}${READLINE_LINE:READLINE_POINT}"
    READLINE_POINT=$(( READLINE_POINT + ${#c} ))
  }
  bind -x '"\es": __cs_insert' 2>/dev/null   # \es = Alt-s
fi

# ── login sync : reconcile the cheatsheet in the background, throttled ────
# Runs at most once per 15 min per machine, detached, never blocks the shell.
if command -v cs-sync >/dev/null 2>&1; then
  __cs_stamp="${XDG_RUNTIME_DIR:-/tmp}/.cs-sync.stamp"
  if [ ! -f "$__cs_stamp" ] || \
     [ $(( $(date +%s) - $(stat -c %Y "$__cs_stamp" 2>/dev/null || echo 0) )) -gt 900 ]; then
    touch "$__cs_stamp" 2>/dev/null
    ( CS_SYNC_QUIET=1 cs-sync >/dev/null 2>&1 & ) 2>/dev/null
  fi
fi
