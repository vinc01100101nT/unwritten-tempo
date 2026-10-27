---
status:
owner: vince
last-updated: 2026-10-16
tags:
  - ai-learnings
---

# 🧠 AI Learnings & Improvements

Lessons about **how the AI works on this project** — habits that paid off, mistakes that cost real time, and the
change of behaviour each one earned. These are working-method notes, not game design.

Two rules keep this folder worth reading:

- **General only.** A lesson lands here when it would help on any codebase — a way of measuring, a way of
  diagnosing, a way of running a long task. Anything true only of this game belongs in a systems doc.
- **Earned, not guessed.** Every entry names the incident that produced it, with what it actually cost. A lesson
  nobody paid for is advice, and advice is not worth filing.

**When to open this folder:** before a SECOND attempt at anything that already failed once — a stalled tool, a
measurement you cannot get, a fix that did not take, a long helper run — and whenever a tool hangs or returns
nothing. That is the moment these pages exist for.

Each page opens with the SYMPTOM that should make you reach for it, then the rule, then the incident and what
it cost, then how to apply it. The protocol itself lives in the project's `CLAUDE.md`.

## The index — find your SYMPTOM, then open the page

You arrive here knowing the symptom and nothing else, so that is what these lines lead with.

| You are seeing this | Open this |
| --- | --- |
| A generated mesh still looks like an earlier build in the editor, though its own data reads right | [The Editor Draws the Mesh You Overwrote](the-editor-draws-the-mesh-you-overwrote.md) |
| You are making new behaviour wait on an existing "is it happening" flag — or a feature works once, then never again until a restart | [A Flag You Gate On Must Actually Clear](a-flag-you-gate-on-must-clear.md) |
| One asset a script authored differs from its siblings (a colour, a flag, a leftover) — or you are writing a load-or-create builder | [Rebuilding Onto an Existing Asset Keeps Its Old Values](rebuilding-onto-an-existing-asset-keeps-its-old-values.md) |
| The user's instruction uses a word you also use for something specific ("volley", "set", "wave", "fix"), or a fix matching your reading of it is still wrong | [The User's Word Is Not My Word](the-users-word-is-not-my-word.md) |
| An editor or program shows its loading screen then closes or freezes on every open, and clearing a cache is the next idea | [An App Crashing on Start: Read Windows' Crash Record First](an-app-crashing-on-start-read-windows-crash-record-first.md) |
| A formula that scores how hard something is to beat explodes at an edge and a cap looks necessary — or a sub-agent hands back such a formula | [A "How Hard Is It" Measure Must List Every Way Round It](a-difficulty-measure-must-list-every-way-round.md) |
| Two copies of a value disagree and you are about to say which one is the owner — or the user says your pick would destroy the tool they author it in | [Two Copies Disagree: the Authoring Tool Is the Owner](two-copies-disagree-the-authoring-tool-is-the-owner.md) |
| The user describes a pose in words and your second version still is not what they meant — or they say you cannot understand them | [A Pose Described in Words Needs a Picture First](a-pose-described-in-words-needs-a-picture.md) |
| You made a new pose, animation, look or glow and are about to put it in the game — or the user says it "looks awkward" and asks if you checked — or they play and still see the OLD look you replaced — or they say your picture shows the opposite of your report — or a glow you reported is too small, washed out or cut by lines in game | [Show a New Look Before Wiring It Into the Game](show-a-new-look-before-wiring-it-in.md) |
| An animation looks right in the pictures or videos you rendered, but in game a foot slides, the body does not travel, or a turn is missing | [Your Own Preview Can Play a Clip Differently From the Game](your-preview-plays-it-differently-from-the-game.md) |
| After a script rebuilt an asset, characters T-pose in game, a material is pink, a field or prefab mesh reads "None", or a rebuilt mesh is not drawn ("does not match the expected mesh data size and vertex stride") — though the asset kept its ID | [Rebuilding an Asset Can Blank Every Reference to It, or Leave It Drawn as Nothing](rebuilding-an-asset-blanks-open-scene-references.md) |
| On Windows, `TZ=Asia/Manila date` prints a time that disagrees with the taskbar or PowerShell clock | [A Time-Zone Clock Reading Comes Back Eight Hours Off](git-bash-ignores-named-time-zones.md) |
| A script run under Linux/WSL fails with `$'\r': command not found`, yet `grep $'\r'` found no carriage returns in it | [A Search for Windows Line Endings Finds None](grep-on-windows-cannot-see-line-endings.md) |
| You changed a few lines of a file with a small script, and the diff against your before-copy shows every line of the file changed | [A Scripted Edit Changed Three Lines, and the Diff Shows the Whole File Changed](a-scripted-edit-rewrote-every-line-ending.md) |
| You are adding detail to something natural (rock, bark, ground) from a reference picture, or the user says a detail looks drawn with a ruler | [Natural Detail Must Be In the Reference](natural-detail-must-be-in-the-reference.md) |
| Values past some index of a Unity shader array read as zero, or the log says `exceeds previous array size … Restart Unity` | [A Shader Array Keeps the Size It Was First Given](a-shader-array-keeps-its-first-size.md) |
| You are about to write "still missing" or ask a question about part of a task the user already agreed to — including offering "keep it" for something their instruction said to remove — or they ask "why isn't X there, it was in the task?" | [An Agreed Item Is Not "Still Missing"](an-agreed-item-is-not-still-missing.md) |
| Unity editor code queued "for the next tick" works while you click in Unity but never runs when the change came from a script, agent or git pull | [Something Queued for the Editor's Next Tick Never Runs](editor-delaycall-never-runs-in-background.md) |
| Something you added to an existing web page renders far too tall, wide or hidden, and your own CSS for it looks right | [A New CSS Class Name Can Already Mean Something](a-new-css-class-can-already-mean-something.md) |
| `git push` of a big commit ends with `Broken pipe` / `the remote end hung up unexpectedly` (exit code may still be 0) | [A Big Push Over SSH Drops the Connection](a-big-push-over-ssh-drops-the-connection.md) |
| `git push` fails with `lookup lfs.github.com: no such host`, but `host lfs.github.com` resolves | [Git LFS Push Says "no such host"](git-lfs-push-says-no-such-host.md) |
| A play-test says only a couple of options ever get picked, or you are building a "random among ready" chooser over options with different costs | [Replay a Chooser With the Real Costs](replay-a-chooser-with-real-costs.md) |
| You are about to back a proposal with "this is how Ragnarok Online (or another game) does it" | [Another Game's Rule, Quoted From Memory, Came Out Backwards](another-games-rule-quoted-from-memory.md) |
| The user said "use X as reference" for designing something new, or says your design is "just a copy of X" | [A Reference Is a Pattern, Not a Template](a-reference-is-a-pattern-not-a-template.md) |
| Someone shows you an editor screen and asks "are we aligned — is this what the game does?" | [Before Saying Aligned, Check Both Readers](before-saying-aligned-check-both-readers.md) |
| A feature you wrote is simply ABSENT at runtime — no error, nothing happens — and the code plainly has it | [The Code Adds It, but What Runs Predates It](the-code-adds-it-the-committed-artifact-doesnt-have-it.md) |
| You run a batch in two steps (build, then patch/wire) over many files, in an app someone else is using | [Step Two Ran on Files Step One Never Rebuilt](step-two-must-read-step-ones-results.md) |
| About to run one script that edits several files and report them all done — or a command came back "rejected" / "interrupted" | [A Script That Edits Many Files Can Stop Halfway](a-script-that-edits-many-files-can-stop-halfway.md) |
| A file you edited with a script from the terminal now holds an invisible character or a zero byte where a backslash was, or a line break inside quotes; or the shell says "parse error" on a script you piped in | [Backslashes Sent Through the Terminal Arrive Changed](backslashes-sent-through-the-terminal-arrive-changed.md) |
| About to report a windowed program as "built but untested" because running it would put a window on the user's screen | [Run a Windowed Program on a Hidden Desktop](run-a-windowed-program-on-a-hidden-desktop.md) |
| A program passed its tests in a harness, and the user's packaged build (single-file, compressed, self-contained) crashes or will not start | [Test the Packaged Form That Ships](test-the-packaged-form-that-ships.md) |
| About to test a start script or wrapper by swapping the program it runs for a fake | [A Fake Program Does Not Fake the Script Around It](a-fake-program-does-not-fake-the-script.md) |
| Something is running that you need to measure, and you cannot see into it | [Observe With a Disposable Second Instance](observe-with-a-disposable-second-instance.md) |
| A command driving a windowed app has hung, and your other requests to that app are timing out too | [A Hanging Tool Is Usually a Dialog](a-hanging-tool-is-usually-a-dialog.md) |
| The Unity editor froze mid-play ("Not Responding", its log just stopped) and you are about to bisect or add log probes | [A Frozen Editor Tells You Where It Is Stuck](a-frozen-editor-tells-you-where-it-is-stuck.md) |
| About to run a build or regenerate script you have not read | [A Rebuild May Delete the Old Output First](a-rebuild-may-delete-the-old-output-first.md) |
| Briefing a long helper run, or one was stopped and you need its status | [Long Agent Runs: Report Per Step, and Ask Them](long-agent-runs-report-per-step.md) |
| A helper's report says something is missing or never happens, and you are about to tell the user | [Check a Helper's Claim Before You Repeat It](check-a-helpers-claim-before-repeating-it.md) |
| A full-screen effect (outlines, ink, fog, grain) looks different through a second camera or a preview, with identical settings | [A Screen Effect Reads the Camera, Not Only Its Settings](a-screen-effect-reads-the-camera-not-only-its-settings.md) |
| Something vanished right after your change, and you are about to say your change broke it | [Two Runs That Took Different Routes Are Not a Before and After](two-runs-that-took-different-routes.md) |
| Something worked before your change and breaks after it, and reading has not told you why | [Bisect by Removing Your Own Change](bisect-by-removing-your-own-change.md) |
| An audit or a bug shows code breaking a rule that is ALREADY written down, and you are about to write the rule again | [A Written Rule That Was Broken Anyway Needs a Check, Not a Second Document](a-written-rule-that-was-broken-needs-a-check.md) |
| Two end states each check out exactly, and the blend or path between them is wildly wrong | [Two Right Answers Blend Into a Wrong Middle](two-right-answers-blend-into-a-wrong-middle.md) |
| You have rebuilt the same thing two or three times and the session is crawling | [Build Last, Build Once](build-last-build-once.md) |
| Replacing how something LOOKS, and about to re-implement its hover, clicks and drag too | [Reuse the Widget's Behaviour, Replace Only Its Look](reuse-the-behaviour-replace-the-look.md) |
| You proved a fix with a hand-built replay (or a replay that printed counts, not the values drawn) and are about to call it fixed | [A Replay of One Piece Passed, and the Real Flow Still Failed](a-replay-of-one-piece-is-not-the-real-flow.md) |
| A parser, probe, census, hand-built test object or screenshot you just made says the thing is corrupt, empty, absent or does nothing | [Prove Your Decoder on a Known-Good Sample](prove-your-decoder-on-a-known-good-sample.md) |
| A script that drives another app produced nothing, and only the app's own log mentions a timeout | [A Busy Host App Never Gets To Your Request](wait-for-the-host-app-to-be-idle.md) |
| Something does nothing at all and there is no error to explain it | [Make the Code Narrate Its Own Decision](make-the-code-narrate-its-decision.md) |
| An app you are driving throws the same error every frame in files you never touched, or a play session dies for no reason | [Something Reloaded Under a Live Session](never-reload-under-a-live-session.md) |
| You hooked a probe into a running editor or app, the user reproduced the bug, and the probe caught nothing | [A Watcher Installed Live Dies on Reload](a-watcher-installed-live-dies-on-reload.md) |
| Your offline compile check passes, then the real build fails on compiler errors | [The Offline Checker Is Not the Editor](the-checker-is-not-the-editor.md) |
| About to repeat something a doc says is current — especially under "still true" — or a code comment (or a helper quoting one) telling you what a setting or an asset is | [A Doc Cannot Vouch for Its Own Freshness](a-doc-cannot-vouch-for-itself.md) |
| You changed a value, read it back, and it had not moved — and you are about to call that a bug | [A Value You Just Changed Reads Stale](a-value-you-just-changed-reads-stale.md) |
| You paused or froze a running system to read it, and everything reads empty | [Put the Recorder Inside — Don't Stop the World](put-the-recorder-inside-dont-stop-the-world.md) |
| The user marks a line, streak, hole, crease or pale patch in a picture — before the first fix, or when a fix left it exactly the same | [Find a Visual Glitch by Casting a Ray Through Its Pixel](find-a-visual-glitch-by-casting-through-its-pixel.md) |
| You added a tool to make a job easier and the job got slower | [You Added a Tool and Everything Got Slower](new-tooling-must-replace-work-not-add-it.md) |
| Every fix round finds a new bug, the user is exhausted, or you are about to hand over a play-test with findings still open | [The Path Table Is the Reviewer's Checklist](the-path-table-is-the-reviewers-checklist.md) |
| You are about to halve or double one constant to hit a target number, and further down the result has something subtracted from it | [Scaling One Side of a Formula That Ends in a Flat Subtraction](scaling-one-side-before-a-flat-subtraction.md) |
| A reviewer or red team says something "traps" or "blocks" the player, and you are about to change the design to remove it | [A Reviewer Called the Design a Bug](a-reviewer-called-the-design-a-bug.md) |
| The user is asking "is there anything else?" for the third or fourth time | [Give the Whole List, Not a Trickle](give-the-whole-list-not-a-trickle.md) |
| A tool printed a summary and deleted the detail, and you want to copy the tool to keep its log | [A Tool Threw Away the Detail You Needed](dont-copy-a-tool-to-keep-its-output.md) |
| A step found another program's file missing or incomplete, it is there when you look afterwards, and it worked on earlier runs | [A File Appearing Is Not Its Writer Finishing](a-file-appearing-is-not-its-writer-finishing.md) |
| A wait-loop never finishes, but the thing it is watching answers fine by hand | [A Wait-Loop Must Know Every Answer](a-wait-loop-must-know-every-answer.md) |
| Generated ground tiles come back too big, too busy, or with a seam, or you are about to write image-generator prompts for tiles | [Image-Generator Tiles: Sizes, Counts and Seams](image-generator-tiles-sizes-counts-seams.md) |
| A generated map or scene follows every instruction and still looks bad — flat blocks, neat evenly spaced shapes | [A Generated Scene Came Back Looking Like a Diagram](image-generator-scenes-come-back-as-diagrams.md) |
| An instruction gives a ratio or share ("a third", "2×") that could apply to more than one quantity — a level or its experience, a price or a rate | [Test Each Reading Against Every Phrase](test-each-reading-against-every-phrase.md) |
| About to report a UI change as smooth or animated, or the user says it still jumps | [Prove Motion With the User's Own Input, Frame by Frame](test-motion-with-the-users-own-input.md) |
| You are about to build a visual effect from a description, or the user saw it and said "not what I meant" | [A Look Agreed in Words Was Rejected on Screen](a-look-agreed-in-words-is-not-agreed.md) |
| Something placed on a picture by coordinates is in the sea, on the wrong terrain, or labelled with a biome it isn't on | [A Placement on a Picture Landed on the Wrong Terrain](check-each-placement-against-the-picture.md) |
| The reader keeps coming back asking "which one?" | [The Reader Keeps Asking "Which One?"](name-the-thing-never-the-position.md) |
| The user does not understand a table of choices about a price or a number | [Show the Starting Numbers in an Options Table](show-the-starting-numbers-in-an-options-table.md) |
| About to guard the place the damage SHOWS, not the place the bad thing was let in | [Fixing a Hole Caused by Another Hole](fixing-a-hole-caused-by-another-hole.md) |
| You added a new reason for something to finish, reusing the machinery that ends it normally — or it keeps finishing and restarting in a loop | [A New Ending Re-runs the Old Ending](a-new-ending-re-runs-the-old-ending.md) |
| You made something travel that never travelled before, and it arrives but LOOKS wrong | [The Far End Reads More Than You Sent](the-far-end-reads-more-than-you-sent.md) |
| Your new second way of doing something completed fine, but what came out is missing something, in the wrong place, or looping | [It Arrived in the Wrong Place](it-arrived-in-the-wrong-place.md) |
| A comment promises an order — "asked for first", "released after", "only once" | [A Comment Promising an Order Is Where the Bug Is](a-comment-promising-an-order-is-where-the-bug-is.md) |
| About to say a cost is cheap, or to explain why it is affordable | [Cheap for Whom, and How Many Times](cheap-for-whom-and-how-many-times.md) |
| You verified a new fallback or defaults file, and the user hit the exact failure it was built to prevent | [You Proved the Fallback — On a Machine That Still Had the Old Answer](tested-while-the-old-answer-was-still-there.md) |
| Your change compiled and loaded, and nothing happened — no error anywhere | [Your Fix Ran on the Wrong Machine](your-fix-ran-on-the-wrong-machine.md) |
| Something new is wearing another thing's name, unit or value, and nothing reported a problem | [A Fallback That Answers With a Real Value](a-fallback-that-answers-with-a-real-value.md) |
| About to call unloading, releasing or deleting something "small work", or to split one packed file into many | [Before Promising to Free Something, Find Everything Made From It](before-promising-to-free-something-find-what-is-made-from-it.md) |
| About to report a count or a size you got from a search, a folder listing or a `du` | [Your Number Is Real, and It Counts the Wrong Thing](your-number-counts-the-wrong-thing.md) |
| Something built for one user now has several, and a widget shows only one of them — or the wrong one | [One Widget, One Subject](one-widget-one-subject.md) |
| A feature has two routes in, and one of them looks like it worked and then quietly comes undone | [One Route Saves, the Other Doesn't](one-route-saves-the-other-doesnt.md) |
| About to offer a fix that depends on a tool or service being ABLE to do one thing you have not seen it do | [Probe the One Capability Before Offering the Plan](probe-the-capability-before-offering-the-plan.md) |
| An alarm keeps firing at you for doing exactly what a rule told you to do, or calls a guard dead that ran | [A Guard That Counts the Work Your Own Rules Demand](a-guard-that-counts-mandated-work.md) |
| A check says something never ran, but it runs fine when you start it by hand | [A Shared Diary Loses Entries When Writers Overlap](a-shared-diary-loses-entries-when-writers-overlap.md) |
| You wrote a check and it passed first time, and you are about to move on | [A Test That Cannot Fail](a-test-that-cannot-fail.md) |
| A log line or probe names an object, and the name makes you believe where it came from | [A Name Is Not an Address](a-name-is-not-an-address.md) |
| You can drive a running system from outside, but it cannot answer the question the bug turns on | [Teach the Harness the Question](teach-the-harness-the-question.md) |
| About to report that a move worked by looking at where it came FROM | [Verify a Transfer at the Destination](verify-a-transfer-at-the-destination.md) |
| You changed a setting or re-saved files, the call said success, and the files still hold the old value | [A Project Rule Overwrote the Setting You Just Wrote](a-project-rule-overwrote-your-setting.md) |
| The user said "do it" to your recommendation, and mid-way you found a reason to do something else | [New Evidence After a Go Goes Back as a Question](new-evidence-after-a-go-goes-back-as-a-question.md) |
| You are about to describe how some OTHER product or game works, and the user is deciding their own design on it | [Check an Outside Fact Before It Becomes a Decision](check-an-outside-fact-before-it-becomes-a-decision.md) |
| Something is buried, clipped or gapped only on things you reused at a new size (a rig, sprite or body) | [A Constant That Hid at One Size Shows at Another](a-constant-that-hid-at-one-size-shows-at-another.md) |
| You wrapped, re-parented or nested something one level deeper, and now something "cannot be opened", is "inactive", or is found as the wrong object | [Moving a Child Breaks the Code That Climbs To It](moving-a-child-breaks-code-that-climbs-to-it.md) |
| A script that adds objects to prefabs said success, but the prefab lacks them — or stray objects with the new names sit in the open scene | [An Object Made While Editing a Prefab Lands in the Open Scene](an-object-made-while-editing-a-prefab-lands-in-the-open-scene.md) |

**No page cap.** A lesson that was paid for gets its own page; the index above is what keeps this skimmable, so keep each symptom line short and distinct. A line is the symptom and the page's title only — the rule, the incident and how to apply it live on the page, so the index stays cheap to read. Merge two pages only when they genuinely share a symptom, and delete one the moment it is obsolete or has graduated into a hard rule.

---
| A running Unity editor refuses to build for another platform ("Unable to build with the current configuration"), or its platform switch reverts a minute later | [A Build for Another Platform Refuses, or the Switch Will Not Stick](build-for-another-platform-in-a-running-editor.md) |
| A Mac refuses an app you built or signed yourself as "damaged", with no "Open Anyway" | [A Mac Says an App You Built Is "Damaged"](mac-app-damaged-unsealed-file.md) |
| You restarted a server after a change and it still behaves like the old code | [A Restart Kept Running the Old Build](a-restart-kept-the-old-build.md) |
| Your fix passed your own numbers and the user says it looks exactly the same — or your number says one thing and their eyes another | [A Fix Passed Your Measurement and the User Says Nothing Changed](measure-what-the-user-sees.md) |
| A character you pose from an editor script stays in its T-pose, or a hand bone reads the same position before and after `Animator.Play`/`Update` | [A Character Stays in Its T-Pose Outside Play](a-character-stays-in-t-pose-outside-play.md) |
| A program needs to know which program started it, or the parent it finds is the system's own starter (process 1) | [A Started Program Cannot Ask Who Started It](a-started-program-cannot-ask-who-started-it.md) |
| A step you are about to hand over says "copy this file from one computer to the other" — and both already pull the same repository | [Two Computers That Share Git Never Copy by Hand](two-computers-that-share-git-never-copy-by-hand.md) |
| About to propose a rule that sorts things onto tiers, each with its own range inside it (a grade and a fold count) | [A Ladder Rule Is Checked Where Two Rungs Meet](a-ladder-rule-is-checked-at-its-rungs-edges.md) |
| Two things go through the same code and only one misbehaves (one creature animates, its sibling stands frozen) | [Same Code, Different Result — Diff the Data](same-code-different-result-diff-the-data.md) |
| An editor script wrote assets and `git status` shows files you never touched (a font, a prefab) | [Save Only the Assets You Changed](save-only-the-assets-you-changed.md) |
| About to show that changing a rule fixes a lopsided fight, and both fighters use that rule | [A Shared Rule Change Moves Both Sides of the Fight](a-shared-rule-change-moves-both-sides.md) |
| A look or effect reported "off everywhere" still shows on one screen | [Turning a Look Off Leaves It On Somewhere Else](turning-a-look-off-misses-a-second-reader.md) |
| About to tell the user why one picture looks better than another | [A Measured Difference May Not Show On Screen](a-measured-difference-may-not-show-on-screen.md) |
| About to delete assets because an id search found nothing using them — or a feature died right after such a deletion | [Deleting Data Nothing Points At Can Still Switch a Feature Off](deleted-data-loaded-by-name.md) |
| A light, fill or tint colour you set looks far darker on screen than its numbers | [A Colour You Typed Comes Out Nearly Black](typed-colours-are-linear.md) |
| A value you saved right after asking the engine to recompute it no longer matches the picture you approved | [A Value Read Right After Asking for a Recompute Is Not the Settled Value](read-a-recomputed-value-after-it-settles.md) |
| About to tune a look or effect by comparing pictures of a few values | [Turn a Knob to Its Extreme Before Tuning It](turn-a-knob-to-its-extreme-first.md) |

## Related

- [The Three-Brain Design Process](../0-process/design-process.md) — the workflow these habits sit inside.
- [Starting the Network](../2-systems/networking/starting-the-network.md) — the runbook several of these lessons were learned against.
