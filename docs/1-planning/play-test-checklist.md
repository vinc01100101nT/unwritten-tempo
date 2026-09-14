---
status: open
owner: vince
last-updated: 2026-09-14
tags:
  - planning-production
---

# Play-Test Checklist

Everything built but never watched working in a running game, in one list. Every step says what to press, what should happen, and what counts as a **FAIL**. It covers four batches:

1. **The bag's columns** (2026-09-14).
2. **Your machine works out no game number** (2026-09-14) — the server sends every number and your screen only draws it.
3. **The gear shape** (2026-09-13) — [Action Plan](action-plan.md) `MT-3` and its subtasks `ST-3-K`, `L`, `N`, `O`, `P`, `S`.
4. **The skill and server plumbing** (2026-09-03) — [Action Plan](action-plan.md) `MT-1`, the half of its list that was never run.

GM commands are typed into chat and need a GM account — see [Starting the Network ▸ GM accounts](../2-systems/networking/starting-the-network.md).

---

## Before you start — what to rebuild and restart

1. **Game server build — REBUILD.** Game code changed in all four batches, and a server only runs the code it was built with. In Unity, let the editor finish compiling, then run `tools/build-game.sh server`.
2. **Game server process — RESTART** after the rebuild. Stop it with `pkill -f 'unwritten-server.app/unwritten'`, check `lsof -nP -iUDP:7777` prints nothing, then start it with the command under *The game server ▸ Running it* in [Starting the Network](../2-systems/networking/starting-the-network.md).
3. **Character server — no restart.** Nothing inside it changed, and the one string both servers must agree on did not change.
4. **Account service — no restart.** Nothing inside it changed.
5. **The game you play — REBUILD** if you play the built game: `tools/build-game.sh client`. Batches 1 and 2 changed what your own screen draws. Playing from the editor instead needs `CHAR_SERVER_URL=http://127.0.0.1:8081` set in the editor's process, or sign-in fails and Play mode ends by itself.

**Never edit a file while a play session is running** — the editor reloads its code mid-play and the session falls over.

---

## 1. The bag's columns

1. Press **B**. Click every tab along the top of the bag in turn.
   *Should:* every tab shows the same three columns — **Name**, **Tier**, **Held**.
   *FAIL:* a tab shows Slot, Skills, Atk/Def or Out, or any tab is missing Held.
2. Type `/weapon sword` and `/item item.minor-healing-potion 3`.
   *Should:* the sword's row reads **1** under Held; the potion row reads **×3**.
   *FAIL:* the sword shows a blank or a dash under Held.
3. Click **Name**, then **Name** again, then a third time. Do the same for **Tier** and **Held**.
   *Should:* first click sorts ascending (▲), second descending (▼), third returns the bag to your own order.
   *FAIL:* a header does nothing, or the third click leaves the list sorted.
4. Type `sword` in the **FIND** box, then clear it and type the name of one skill the sword rolled.
   *Should:* both searches find the sword.
   *FAIL:* searching a skill name finds nothing.

## 2. Your machine works out no game number

1. Hit a creature ten times with basic attacks.
   *Should:* every hit draws exactly one number (or **MISS**), and the creature's health bar drops on each hit that shows a number.
   *FAIL:* a hit draws two numbers, or a number appears while the bar does not move.
2. Kill a creature with a multi-hit sword skill part-way through its hits.
   *Should:* the hits after the kill still draw numbers on the body, and nothing else happens to it.
   *FAIL:* those hits apply a status, a buff or a kill a second time.
3. Take some damage, then use the potion from step 1.2 (double-click its row).
   *Should:* green **+N** digits appear over you once, and your health bar rises by that amount.
   *FAIL:* no green digits, digits twice, or the bar does not move.
4. Stand still after taking damage.
   *Should:* health refills steadily with no jumps backwards.
   *FAIL:* the bar climbs and then snaps back down.
5. Press a skill on your hotbar, then press it again straight away, then wait for its timer.
   *Should:* the second press is refused; the skill becomes usable exactly when its timer runs out.
   *FAIL:* the second press fires, or the skill stays unusable after its timer ends.
6. Press **C**, type `/statpts 5`, spend one point.
   *Should:* every number on the sheet that depends on that stat updates within about half a second.
   *FAIL:* the sheet keeps the old numbers until you close and reopen it.
7. Press **P**.
   *Should:* nothing opens — the old live-tuning panel is gone.
   *FAIL:* a panel opens.

## 3. The gear shape (`MT-3`)

1. Press **C**. *(ST-3-P)*
   *Should:* the sheet shows **MELEE ATK**, **RANGED ATK** and **M.ATK** on their own lines, and beside each main stat a **next +N at X**.
   *FAIL:* a single ATK line, a missing "next" note, or clipped text.
2. Type `/statpts 30`. Raise **STR** one point at a time, watching MELEE ATK, until STR goes from 9 to 10. *(ST-3-O)*
   *Should:* each ordinary point adds 1; the point that reaches 10 adds **5**.
   *FAIL:* the step to 10 adds 1 or 2.
3. Holding the sword, raise **DEX** by two points. *(ST-3-N)*
   *Should:* RANGED ATK rises 1 per point; MELEE ATK rises 1 in total across the two points.
   *FAIL:* MELEE ATK rises on every DEX point.
4. Type `/item item.bow`, double-click the bow to wear it, hit a creature, raise DEX, hit it again. *(ST-3-N)*
   *Should:* the hits grow with DEX.
   *FAIL:* only STR makes the bow's hits grow.
5. Hover the sword, then the bow.
   *Should:* the sword's card says **MELEE attack**; the bow's says **RANGED attack**.
   *FAIL:* the line is missing.
6. Open the bag and the character sheet. *(ST-3-K)*
   *Should:* no accessory place appears anywhere.
   *FAIL:* "Accessory" appears.
7. Type `/weapon sword common`, `/weapon sword epic`, `/weapon sword legendary`. Hover each.
   *Should:* ATK reads **3**, **4** and **4**. (A Rare sword also reads 3 — a 9% lift on 3 still rounds to 3.)
   *FAIL:* the Epic or Legendary sword reads 3.
8. Fight a creature for about a minute, taking hits.
   *Should:* now and then a much bigger hit lands on you with the crit sound and effect.
   *FAIL:* every hit falls in one narrow band, or a big hit looks and sounds ordinary.
9. Hit a creature with basic attacks.
   *Should:* each hit's effect plays once.
   *FAIL:* the effect plays twice.

**Cannot be watched in play today, and why:**

- **ST-3-S (armour rolls when it drops)** — no armour item exists yet; the first pieces are `MT-7`. Once one is authored with ranges: grant it five times, the copies should differ; wear one, log out and back in, drop it and pick it up — its numbers should never change.
- **ST-3-L (creatures count their gear's main stats)** — the only creature wearing gear is the Axolot, and its Sword carries no main stats, so there is nothing to see move.

## 4. The skill and server plumbing (`MT-1`)

Open the passive picker with **K** to tick or untick a passive, and the Grimoire with **J** to put any skill on your bar. `/bind <skillId>` does the same from chat.

**Settle this first — the one result that came back neither passed nor failed:**

1. **B1 — a passive on a landed basic attack.** Tick **Momentum** in K. Land basic attacks on a creature.
   *Should:* a **Mo** tile appears on your buff bar and its count climbs with each landed hit.
   *FAIL:* nothing appears.
2. **B2 — a passive on a kill.** Tick **Encore** in K, put **Blade Dance** on your bar, kill a creature with Blade Dance.
   *Should:* Encore's effect (read its card in K) fires on the kill.
   *FAIL:* nothing happens on the kill.

**Then the rest:**

3. **A5** — type `/weapon sword rare`, wear it, press a skill from its kit. *Should:* the skill fires on the first press. *FAIL:* the tile is blank or the press does nothing.
4. **A6** — double-click a potion in the bag. *Should:* it is used once and the stack drops by one. *FAIL:* nothing happens, or two go.
5. **A8** — press skills until your mana is below the next skill's cost, then press it. *Should:* refused, no mana spent. *FAIL:* it fires.
6. **B3** — put **Parry** on your bar, tick **Composure** and **Opening** in K. Press Parry just before a creature hits you. *Should:* the hit is erased, and Composure fires. *FAIL:* the hit lands, or Composure never fires.
7. **B4** — tick **Last Stand**, type `/heal`, then let a creature bring you below 30% health. *Should:* Last Stand fires once as you cross the line. *FAIL:* it never fires, or fires on every hit below the line.
8. **B5** — mid-fight, tick a passive you have not used this session and trigger it. *Should:* it fires the first time. *FAIL:* it needs a relog to work.
9. **B6** — untick it and trigger the same moment. *Should:* it no longer fires. *FAIL:* it still fires.
10. **C2** — put **War Banner** or **Bulwark** on your bar from J and press it. Neither has a two-letter tag. *Should:* its buff tile shows the skill's full name. *FAIL:* a blank tile.
11. **C4** — press **Brace**, and while its tile is up press another skill that grants a buff, let that one run out, then hover Brace's tile. *Should:* Brace's hover text is still complete. *FAIL:* the text is gone or blank.
12. **C5** — let a buff expire, then press the same skill again. *Should:* the tile comes back with its timer. *FAIL:* no tile the second time.
13. **C6** — press **Brace** and take hits while it is up. *Should:* the red tile that follows carries the same letters, **Br**. *FAIL:* different letters, or blank.
14. **C7** — press **Parry** several times before being hit. *Should:* the tile's count rises with each press and falls by one per hit erased. *FAIL:* the count never rises past 1, or does not fall.
15. **D2** — press a skill, swing, and double-click a potion within about a second. *Should:* all three happen. *FAIL:* one is silently dropped.
16. **D3** — kill a creature that drops something. *Should:* the item appears in your bag without picking anything up. *FAIL:* the bag does not change.
17. **E1** — walk up to a hostile creature and walk away. *Should:* it chases you. *FAIL:* it stands still.
18. **E2** — walk through a portal to another map. *Should:* the new map has creatures in it. *FAIL:* the map is empty.
19. **E4** — let a creature kill you. *Should:* it stops attacking once you are dead. *FAIL:* it keeps swinging at your body.

**Needs two players at once** — start a second copy of the game with the command in [Starting the Network ▸ 5. The players](../2-systems/networking/starting-the-network.md), signed in on a second account:

20. Both stand in the same map. *Should:* each sees the other walking and swinging. *FAIL:* either is invisible to the other.
21. Stand in different maps. *Should:* neither sees the other. *FAIL:* a player from another map appears.
22. Both attack one creature. *Should:* it picks one of you and hits that person; both of you see its health drop. *FAIL:* one player's screen shows its health frozen.
