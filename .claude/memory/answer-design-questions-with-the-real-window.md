---
name: ""
metadata: 
  node_type: memory
  originSessionId: 12ad0785-77dd-4336-9b5f-6d8b08140a50
---

When the user asks a STRUCTURAL design question — "where should this live?", "does it earn its own entry?",
"a new effect or a checkbox?", "what will I see?" — the ground truth is the **authoring window they actually
look at**, not the runtime architecture. Open the editor window's drawing code FIRST, then answer with an
**ASCII mockup of that window**, with every label copied verbatim from the code.

**Why:** measuring one layer and inferring about another is RULE ZERO wearing a disguise. In the session that
produced this, I read the state store, the stack gate, the trigger list and the damage door thoroughly and got
every runtime fact right — then told the user "this needs a new state but NOT a new effect kind." Six turns of
argument later I finally opened the composer and found that the visible tree is `State ▸ Buffs ▸ [which state]`
and that list IS the effect-kind list. Parry and Brace are each one entry there. So "a new state without a new
effect" was a distinction that exists nowhere on screen — invented, and stated with the confidence of the
verified runtime work sitting next to it. The user's very first guess had been right the whole time.

**Three rules that fall out of it:**

1. **Never coin a noun for something that already has an on-screen name.** I said "pile" for six turns; the
   user read it as "buff" and we were describing different objects the whole time. "Pile" is one dropdown's
   internal label, not the concept's name. Needing a word the user hasn't used is the signal that I haven't
   found the real one yet — go read the window and use ITS word. (See [[name-knobs-concretely-one-job]].)
2. **Lead with the mockup, not the prose.** Every prose attempt made it worse; one drawn window ended the
   disagreement in a single turn. For any "what would this look like" question, the mockup IS the answer and
   the prose is the footnote.
3. **When their sentence is ambiguous and I have just built a model, ASK.** I misread "consecutive hits divided
   by 3" twice — both times by pattern-matching their words onto the model I had just finished building, then
   arguing at length against a position they never held. One clarifying question costs a line; arguing against
   an invented position costs the turn AND the trust.

**The tell to watch for:** if I am about to explain why the user's understanding is wrong, and my explanation
rests on a layer I have NOT opened this session — stop and open it. Being right about the runtime does not
make me right about the window.

Related: [[existing-function-name-is-not-the-behaviour]], [[never-widen-a-snapshot-into-a-verdict]].
