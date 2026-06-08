# bash completion for cs
_cs() {
  local cur prev words cword
  _init_completion 2>/dev/null || {
    cur="${COMP_WORDS[COMP_CWORD]}"; prev="${COMP_WORDS[COMP_CWORD-1]}"
  }
  local subs="add list stats render import check lint sync rm edit search"
  local datadir="${CS_DATA_DIR:-$HOME/.local/share/cs/data}"

  if [ "$COMP_CWORD" -eq 1 ]; then
    # first arg: a subcommand OR a category (for search)
    local cats=""
    [ -d "$datadir" ] && cats=$(cd "$datadir" && ls *.jsonl 2>/dev/null | sed 's/\.jsonl$//')
    COMPREPLY=( $(compgen -W "$subs $cats" -- "$cur") )
    return
  fi

  local sub="${COMP_WORDS[1]}"
  case "$sub" in
    add)
      COMPREPLY=( $(compgen -W "--command --name --desc --category --tags \
        --example --explanation --gotcha --portability --source --related \
        --on-dup --json --stdin --reviewed --dry-run" -- "$cur") ) ;;
    render|list)
      local cats=""
      [ -d "$datadir" ] && cats=$(cd "$datadir" && ls *.jsonl 2>/dev/null | sed 's/\.jsonl$//')
      COMPREPLY=( $(compgen -W "$cats --all --out" -- "$cur") ) ;;
    import)
      COMPREPLY=( $(compgen -f -- "$cur") ; compgen -W "--category --report --force --dry-run" -- "$cur") ;;
    check)
      COMPREPLY=( $(compgen -W "--approve --lint-only" -- "$cur") ) ;;
    search)
      COMPREPLY=( $(compgen -W "--tag --print" -- "$cur") ) ;;
  esac
}
complete -F _cs cs
