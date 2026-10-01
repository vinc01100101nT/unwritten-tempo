# A Search for Windows Line Endings Finds None, and the Script Still Fails

**Reach for this when** a search for carriage returns (`grep $'\r'`) says a file has none, but running it under
Linux or WSL still prints `$'\r': command not found`, `invalid option set: -` or `unexpected end of file`.

## The rule

The `grep` that ships with Git for Windows ignores the carriage return at the end of each line, so it cannot find
one. It reports zero even when every line has one. Ask git or read the raw bytes instead:
`git ls-files --eol` names every file whose copy on disk has Windows line endings (`w/crlf`).

## Why

2026-10-01: asked how to run a project script from Ubuntu, I searched the scripts in the tools folder for carriage
returns. `grep` found none, so I told the user they would not hit the line-ending error. They ran the character
server's start script on the next turn and got exactly that error. `git ls-files --eol` then listed 8 of the
project's 16 shell scripts with Windows line endings, the tools scripts I had "checked" among them. `od -c` showed
`\r\n` on line 1 of a file `grep -c $'\r'` counted 0 in. The user spent a round on a promise that was false.

## How to apply

1. List the affected files: `git ls-files --eol -- '*.sh' | grep w/crlf`.
2. To look at one file's bytes: `head -c 200 <file> | od -c` and look for `\r \n`.
3. Never use `grep` on Windows as proof that a file has no carriage returns.
4. Windows line endings appear in files on disk when `core.autocrlf` is `true`, even though git stores the files
   with Unix endings. A `*.sh text eol=lf` line in `.gitattributes` makes git write shell scripts to disk with
   Unix endings on every checkout.
