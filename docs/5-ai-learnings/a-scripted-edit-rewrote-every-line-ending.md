# A Scripted Edit Changed Three Lines, and the Diff Shows the Whole File Changed

**Reach for this when** you edited a file with a small script (Python, sed) and a diff against your before-copy — or
`git diff --stat` — reports every line of the file removed and added again, though you changed a handful.

## The rule

A script that reads a file as text and writes it back can rewrite every line ending without saying so. Read and
write the BYTES, and assert the count of Windows line endings is the same after the edit as before it. Count them from
the bytes, never with `grep`.

## Why

2026-10-12, on Windows: I made several small replacements with Python using
`open(path, encoding='utf-8').read()` and `open(path, 'w', encoding='utf-8', newline='').write(text)`. Reading in
text mode turns every `\r\n` into `\n`; writing with `newline=''` writes them back as plain `\n`. Two files that had
Windows line endings on all of their lines (1,427 and 141 lines) were silently converted. Nothing failed: the code
compiled and every check passed. It was found only because the diff against the before-copy reported 1,427 lines
removed for an edit that added 64. Left alone, the next commit would have shown both whole files as rewritten and
buried the real change. Six other files edited the same way had Unix endings already and were unharmed — which is
why the habit looked safe.

## How to apply

1. Edit in bytes: `b = open(p, 'rb').read(); s = b.decode('utf-8'); … ; open(p, 'wb').write(s.encode('utf-8'))`.
2. Before writing, assert nothing but the intended text moved:
   `assert out.count(b'\r\n') == b.count(b'\r\n')`.
3. Text inserted into a file with Windows endings must carry them too: build it with `\n`, then
   `.replace('\n', '\r\n')` when the file's own count is above zero.
4. After any scripted edit, compare line counts against the before-copy:
   `diff before after | grep -c '^<'` must be about the number of lines you meant to change, not the file's length.
5. To repair a file that was fully Windows-ended and is now fully Unix-ended: read bytes, check
   `b.count(b'\r\n') == 0`, write `b.replace(b'\n', b'\r\n')`, then diff against the before-copy again.
