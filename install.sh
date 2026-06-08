#!/usr/bin/env bash
# install.sh — wire up cs on a Linux/macOS box.
#   * symlink cs + cs-sync onto $PATH
#   * configure git for clean concurrent-append merges
#   * install a daily sync (systemd --user timer, else cron)
#   * install bash completion
#   * print the shell/zellij integration snippets (Phase 5)
#
# Idempotent: safe to re-run. Run from anywhere.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN="${XDG_BIN_HOME:-$HOME/.local/bin}"
mkdir -p "$BIN"

echo "cs install — repo: $REPO"

# ── 1. binaries ──────────────────────────────────────────────────────────
chmod +x "$REPO/cs" "$REPO/cs-sync"
ln -sf "$REPO/cs"      "$BIN/cs"
ln -sf "$REPO/cs-sync" "$BIN/cs-sync"
echo "  ✓ linked cs, cs-sync -> $BIN"

case ":$PATH:" in
  *":$BIN:"*) ;;
  *) echo "  ⚠ $BIN is not on \$PATH — add: export PATH=\"$BIN:\$PATH\"" ;;
esac

# ── 2. git config for the store ──────────────────────────────────────────
if [ -d "$REPO/.git" ]; then
  git -C "$REPO" config merge.union.name  "line-union" 2>/dev/null || true
  git -C "$REPO" config merge.union.driver "true"      2>/dev/null || true
  git -C "$REPO" config rerere.enabled true            2>/dev/null || true
  echo "  ✓ git configured (union merge for data/*.jsonl)"

  # Ensure this clone mirrors its pushes to the GitHub mirror, so no machine
  # becomes a data island regardless of which remote it was cloned from.
  # Additive + idempotent: never removes a push URL; re-running is a no-op.
  GH_MIRROR="https://github.com/LexingtonStanley/terminal-cheatsheet.git"
  if git -C "$REPO" remote get-url origin >/dev/null 2>&1; then
    if ! git -C "$REPO" remote get-url --push --all origin 2>/dev/null | grep -qxF "$GH_MIRROR"; then
      # Adding the first explicit pushurl drops the implicit (fetch) one — so if
      # none is set yet, pin the current fetch URL as a pushurl before adding GH.
      if [ -z "$(git -C "$REPO" config --get-all remote.origin.pushurl)" ]; then
        git -C "$REPO" remote set-url --add --push origin \
          "$(git -C "$REPO" remote get-url origin)" 2>/dev/null || true
      fi
      git -C "$REPO" remote set-url --add --push origin "$GH_MIRROR" 2>/dev/null \
        && echo "  ✓ GitHub mirror added as a push target"
    fi
  fi
fi

# ── 3. daily sync ────────────────────────────────────────────────────────
if command -v systemctl >/dev/null 2>&1 && systemctl --user show-environment >/dev/null 2>&1; then
  UD="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
  mkdir -p "$UD"
  cat > "$UD/cs-sync.service" <<EOF
[Unit]
Description=Sync cs command cheatsheet with remotes
After=network-online.target

[Service]
Type=oneshot
Environment=CS_HOME=$REPO CS_SYNC_QUIET=1
ExecStart=$BIN/cs-sync
EOF
  cat > "$UD/cs-sync.timer" <<EOF
[Unit]
Description=Daily cs cheatsheet sync

[Timer]
OnCalendar=daily
Persistent=true
RandomizedDelaySec=600

[Install]
WantedBy=timers.target
EOF
  systemctl --user daemon-reload 2>/dev/null || true
  systemctl --user enable --now cs-sync.timer 2>/dev/null || true
  echo "  ✓ systemd --user timer installed (daily) — check: systemctl --user list-timers cs-sync.timer"
elif command -v crontab >/dev/null 2>&1; then
  LINE="@daily CS_HOME=$REPO CS_SYNC_QUIET=1 $BIN/cs-sync >/dev/null 2>&1"
  ( crontab -l 2>/dev/null | grep -v 'cs-sync'; echo "$LINE" ) | crontab -
  echo "  ✓ cron @daily sync installed"
else
  echo "  ⚠ no systemd/cron — run 'cs sync' manually or add your own scheduler"
fi

# ── 4. bash completion ───────────────────────────────────────────────────
if [ -f "$REPO/completions/cs.bash" ]; then
  CD="${XDG_DATA_HOME:-$HOME/.local/share}/bash-completion/completions"
  mkdir -p "$CD"
  ln -sf "$REPO/completions/cs.bash" "$CD/cs"
  echo "  ✓ bash completion linked"
fi

# ── 5. integration snippets (shell login-sync + hotkeys) ─────────────────
cat <<EOF

cs is installed. Two integrations to wire into your dotfiles (~/.loki-term):

  1. On-login sync (background, throttled) — add to your shell rc:
       (cs sync &)   # or source $REPO/integrations/login-sync.sh

  2. Hotkeys (see $REPO/integrations/):
       • zellij Alt-/  -> floating cs pane   (zellij.kdl snippet)
       • ble.sh Alt-s  -> insert command onto prompt line (loki-shell snippet)

Run 'cs' to search, 'cs check' to review the imported seed entries.
EOF

# ── 6. sanity ────────────────────────────────────────────────────────────
python3 "$REPO/cs" check --lint-only >/dev/null 2>&1 && echo "  ✓ cs check passed" \
  || echo "  ⚠ cs check reported issues — run 'cs check'"
