# Source this — never run it — to load settings.env into the current shell:
#
#     REPO=<the top of the project>; . "$REPO/tools/load-settings.sh"
#
# THE ONE WAY a script here reads the project's settings file, so every script loads it the same way and the
# rules below hold everywhere:
#
#   - A value ALREADY SET in the shell wins over the file. So a setting typed in front of a command still does
#     what it says, exactly as it did before there was a file.
#   - The file is READ, never run. `. settings.env` would execute it, and a secret containing a `$` or a
#     backtick would be mangled — or worse. Each line is split at its first `=` and taken literally.
#   - A missing file is not an error here; each script decides what it cannot do without a setting.
#
# Where the file is: settings.env at the top of the project ($REPO, which the caller sets), or the path in
# SETTINGS_FILE.

__settings_file="${SETTINGS_FILE:-$REPO/settings.env}"
if [ -f "$__settings_file" ]; then
  while IFS= read -r __line || [ -n "$__line" ]; do
    __line="${__line#"${__line%%[![:space:]]*}"}"
    case "$__line" in ''|'#'*) continue ;; esac
    __line="${__line#export }"
    __key="${__line%%=*}"
    [ "$__key" = "$__line" ] && continue
    case "$__key" in ''|*[!A-Za-z0-9_]*) continue ;; esac
    __value="${__line#*=}"
    # A value written in double quotes, as the other two readers of this file (Node and Caddy) also accept.
    case "$__value" in \"*\") __value="${__value#\"}"; __value="${__value%\"}" ;; esac
    if [ -z "$(eval "printf '%s' \"\${$__key:-}\"")" ]; then
      export "$__key=$__value"
    fi
  done < "$__settings_file"
fi
unset __settings_file __line __key __value
