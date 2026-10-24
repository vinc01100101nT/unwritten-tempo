// Prints the skill balance report from skills-data.js through formula.js, and writes the status page
// data/skill-balance.md. Run it through extract.py, which refreshes the data first.
const { BALANCE, balanceEvaluate, setPool } = require("./formula.js");
const skills = require("./skills-data.js");
setPool(skills);

const pct = v => (v >= 0 ? "+" : "") + Math.round(v) + "%";
const tag = s => s.name + (s.ult ? " *" : "") + (s.passive ? " (per s)" : "");
const line = (s, r) => `  ${s.weapon.padEnd(6)} ${tag(s).padEnd(22)} ${pct(r.off).padStart(6)}   value ${r.value.toFixed(2).padStart(6)}  budget ${r.budget.toFixed(2).padStart(6)}`;

// "follow_hit: SubStatBuff MovementSpeed on you" → "a movement speed buff on you"
const words = s => s.replace(/^([HM])PRegen$/, "$1P regen").replace(/([a-z])([A-Z])/g, "$1 $2")
  .replace(/^(?!HP|MP)(.*)$/, m => m.toLowerCase());
const article = w => (/^([aeiou]|MP|HP)/i.test(w) ? "an " : "a ") + w;
const MOMENTS = { OnLowHP: "dropping to low HP", OnKill: "a kill", OnDodge: "a dodge", OnParry: "a parry",
                  OnBasicAttackStart: "starting a basic attack" };
const nameOf = Object.fromEntries(skills.map(s => [s.id, s.name]));
function plain(r) {
  let t = r.replace(/^[\w]+: /, "")
    .replace(/skill\.[\w.]+/g, id => nameOf[id] || id)
    .replace(/\b(On\w+)\b/g, m => MOMENTS[m] || m)
    .replace(/^how often (.+?) \((.+?)\) happens/, "how often $1 by $2 happens");
  const m = t.match(/^SubStat(Buff|Debuff) (\w+) on (you|them)$/);
  if (m) return `${article(words(m[2]))} ${m[1] === "Buff" ? "buff" : "cut"} on ${m[3]}`;
  const k = t.match(/^(\w+) on (you|them)$/);
  if (k) return `${words(k[1])} on ${k[2]}`;
  return t;
}
const listOf = arr => arr.map(plain).filter((v, i, a) => a.indexOf(v) === i).join("; ");

const ev = skills.map(s => [s, balanceEvaluate(s)]);
const byOff = (a, b) => Math.abs(b[1].off) - Math.abs(a[1].off);
const within = r => Math.abs(r.off) <= BALANCE.tolerance;
const priced = ev.filter(([, r]) => r.status === "priced").sort(byOff);
const prov = ev.filter(([, r]) => r.status === "provisional").sort(byOff);
const partly = ev.filter(([, r]) => r.status === "partly priced").sort(byOff);
const none = ev.filter(([, r]) => r.status === "not priced");
const balanced = priced.filter(([, r]) => within(r)), balancedProv = prov.filter(([, r]) => within(r));
const off = [...priced, ...prov].filter(([, r]) => !within(r)).sort(byOff);

console.log("Skill balance report — STEP 1 (simple version). The full balancing system is planned and waiting:");
console.log("docs/2-systems/active-skill-system-v3/skill-balancing-system-plan.md");
console.log(`Balanced within ±${BALANCE.tolerance}%. * = ultimate. "(per s)" = a passive, priced per second of fighting.`);
console.log("PROVISIONAL = priced with at least one guess about how a fight goes, until play-testing settles it.\n");
console.log("OFF BUDGET:");
off.forEach(([s, r]) => console.log(line(s, r) + (r.status === "provisional" ? "   provisional" : "")));
if (!off.length) console.log("  none");
console.log("\nBALANCED:");
balanced.forEach(([s, r]) => console.log(line(s, r)));
console.log("\nBALANCED, PROVISIONAL:");
balancedProv.forEach(([s, r]) => console.log(line(s, r)));
console.log("\nPARTLY PRICED — the % counts only the priced parts:");
partly.forEach(([s, r]) => console.log(line(s, r) + "\n      not priced: " + listOf(s.unpriced)));
console.log("\nNOT PRICED:");
none.forEach(([s]) => console.log(`  ${s.weapon.padEnd(6)} ${tag(s).padEnd(22)} ${listOf([...s.reasons, ...s.unpriced])}`));
console.log(`\nRead ${skills.length} weapon skills: ${balanced.length} balanced, ${balancedProv.length} balanced provisionally, ` +
            `${off.length} off budget, ${partly.length} partly priced, ${none.length} not priced.`);

// ---- the status page --------------------------------------------------------------------------------------------
const row = (s, r) => `| ${s.weapon} | ${s.name}${s.ult ? " (ultimate)" : ""}${s.passive ? " (passive, per second)" : ""} | ${pct(r.off)} | ${r.value.toFixed(2)} | ${r.budget.toFixed(2)} | ${listOf(s.extras || []) || "—"} |`;
const head = "| Weapon | Skill | Off budget | Value | Budget | Extra on top (an investment, not priced) |\n| --- | --- | --- | --- | --- | --- |";
const table = arr => arr.length ? [head, ...arr.map(([s, r]) => row(s, r))] : ["None."];
const G = BALANCE;
// ---- the price list: what one unit of each thing is worth, read from the same constants the formula uses ----------
const swings = {};
skills.forEach(s => { if (s.swing && !(s.weapon in swings)) swings[s.weapon] = s.swing; });
const n2 = v => Number(v.toFixed(4)).toString();
const perSwing = k => Object.entries(swings).map(([w, sw]) => `${w} ${n2(k / sw)}`).join(" · ");
const swingNote = Object.entries(swings).map(([w, sw]) => `${w} ${n2(sw)} s`).join(", ");
const priceRows = [
  ["**What a skill pays**", "", ""],
  ["Cooldown", "1 s", n2(G.rate)],
  ["Cooldown of an ultimate", "1 s", n2(G.ultRate)],
  ["Mana", "1 MP", n2(G.manaRate)],
  ["Cast time, or the time the skill holds you in place", "1 s", perSwing(1)],
  ["A passive's allowance", "1 s of fighting", n2(G.passiveRate)],
  ["**What a skill gives**", "", ""],
  ["Damage", "100% of a basic attack, one hit, one enemy", "1"],
  ["… landing 8 m or more away", "each hit", `× ${G.ranged}`],
  ["… a wind-up held to the end", "each hit", `× ${G.releaseEarly}`],
  ["A hit stopped (a parry charge)", "1 hit", "1"],
  ["Your next attacks hit harder", "+10%, one attack", n2(0.1)],
  ["An enemy's next attacks hit softer", "−10%, one attack, one enemy", n2(0.1)],
  ["Enemy attack speed or accuracy cut (Static too)", "−10% for 1 s, one enemy", n2(0.1 * G.enemyRate)],
  ["Enemy defence or magic defence cut", "−10% for 1 s, one enemy", perSwing(0.1)],
  ["Your attack speed or damage up", "+10% for 1 s", perSwing(0.1)],
  ["Your defence (or magic defence) up", "+10% for 1 s", n2(0.1 * G.enemyRate * G.physicalShare)],
  ["Mana drained to you", "1 MP", n2(G.manaRate)],
  ["Your MP regen up", "+1 MP a second, for 1 s", n2(G.manaRate)],
  ["**Priced with a guess (provisional)**", "", ""],
  ["Movement speed up, or a slow on an enemy", "10% for 1 s (each enemy, for a slow)", perSwing(0.1 * G.positioning)],
  ["Dash or teleport", "1 m covered", perSwing(G.positioning / G.runSpeed)],
  ["Heal as a share of max HP", "10% of max HP", n2(0.1 / G.blowShare)],
  ["Flat HP: a heal, HP regen, a burn / poison / bleed, their regen cut", "10 HP (each enemy, for damage)", n2(10 / G.refAttack)],
  ["Your accuracy up (stops counting past the miss rate)", `+10% for 1 s, up to +${G.missRate * 100}%`, perSwing(Math.min(0.1, G.missRate))],
  ["Your evasion up", "+10% for 1 s", n2(0.1 * G.enemyRate)],
  ["A miss prevented (sure hit)", "1 attack", n2(G.missRate)],
  ["Taunt", "1 s, one enemy", n2(G.tauntPerSec)],
  ["Cleanse", "one use", n2(G.cleanseWorth)],
  ["A mark placed (Chill, copied Chill)", "1 mark, one enemy", n2(G.markBonus * G.markCash)],
  ["Marks extended", "1 mark, one enemy", n2(G.markBonus * G.markCash * 0.25)],
  ["Burn put out, payoff", "payoff 1, one enemy", n2(G.burnLeftHp / G.refAttack)],
  ["Disables turned away", "1 s", n2(G.disableSecs / G.disableEvery * G.enemyRate)],
  ["Another skill reset, lengthened or given charges", "one use", "that skill's own cost or worth"],
];
const firingRows = [
  ["Each of your basic attacks", perSwing(1)],
  ["An enemy blow aimed at you", n2(G.enemyRate)],
  ["A dodge (guess)", n2(G.enemyRate * G.dodgeShare)],
  ["A kill (guess)", n2(1 / G.killEvery)],
  ["Dropping to low HP (guess)", n2(1 / G.lowHpEvery)],
];
const md = [
  "# Skill Balance — status",
  "",
  "**GENERATED by `python tools/skill-balance/extract.py` from the saved skill files. Do not edit — the next run",
  "overwrites it.** This is step 1, the simple balance report; the full system is planned in",
  "[Skill Balancing System — Plan](../docs/2-systems/active-skill-system-v3/skill-balancing-system-plan.md). Every",
  "number comes from the same formula the calculator page uses. A skill is **balanced** when what it gives is within",
  `±${G.tolerance}% of what it costs, counted in basic attacks. "Balanced" means balanced against the formula, not yet`,
  "proven in play.",
  "",
  "**Provisional** means the price uses at least one guess about how a fight goes. The guesses, all to be settled by",
  "play-testing:",
  "",
  `- being in the right place matters in ${G.positioning * 100}% of a fight (slows, movement speed, dashes); a dash is worth the run it saves at ${G.runSpeed} m a second (measured), the whole range when aimed at a spot and half of it when aimed at a target`,
  `- an enemy blow takes ${G.blowShare * 100}% of your max HP`,
  `- an ordinary attack misses ${G.missRate * 100}% of the time; you dodge ${G.dodgeShare * 100}% of enemy blows`,
  `- a placed mark is cashed in ${G.markCash * 100}% of the time, for ${G.markBonus} of a basic attack`,
  `- a kill every ${G.killEvery} s; low health every ${G.lowHpEvery} s; a ${G.disableSecs} s disable every ${G.disableEvery} s`,
  `- flat HP (burn, regen) is measured against an attack of ${G.refAttack} (level 50, before gear); a burn put out has ${G.burnLeftHp} HP left`,
  `- a cleanse is worth ${G.cleanseWorth} of a basic attack; a second of taunt ${G.tauntPerSec}`,
  "",
  "**Extra on top** is growth that needs an investment (a built-up pile, hit chance, speed, max MP) — the base must be",
  "balanced, and the extra is the reward for building into it.",
  "",
  "## Price list — basic attack = 1",
  "",
  "What one unit of each thing is worth, in basic attacks. A skill's cost is the sum of the first block; what it gives",
  "is the sum of the rest. Where a weapon's swing time matters the worth is given per weapon (" + swingNote + " a",
  "swing), because a second of a buff covers fewer basic attacks on a slower weapon. Bigger amounts scale straight:",
  "+20% for 3 s is 6 times the +10% for 1 s row.",
  "",
  "| Thing | One unit | Worth (basic attacks) |\n| --- | --- | --- |",
  ...priceRows.map(r => `| ${r.join(" | ")} |`),
  "",
  "A passive is worth one firing × how often its moment comes, and must match its allowance of " + n2(G.passiveRate) +
    " a second. How often each moment comes, per second of fighting:",
  "",
  "| Moment | Times a second |\n| --- | --- |",
  ...firingRows.map(r => `| ${r.join(" | ")} |`),
  "",
  `**${skills.length} weapon skills:** ${balanced.length} balanced · ${balancedProv.length} balanced provisionally · ` +
    `${off.length} off budget · ${partly.length} partly priced · ${none.length} not priced.`,
  "",
  `## Balanced (${balanced.length})`, "", ...table(balanced), "",
  `## Balanced, provisional (${balancedProv.length})`, "", ...table(balancedProv), "",
  `## Off budget (${off.length})`, "", `More than ${G.tolerance}% off — fixable with one number.`, "", ...table(off), "",
  `## Partly priced (${partly.length})`, "",
  "The % counts only the parts the formula can price; the rest needs a judgement.", "",
  "| Weapon | Skill | Off budget (priced part) | Not priced |\n| --- | --- | --- | --- |",
  ...partly.map(([s, r]) => `| ${s.weapon} | ${s.name}${s.ult ? " (ultimate)" : ""} | ${pct(r.off)} | ${listOf(s.unpriced)} |`),
  "",
  `## Not priced (${none.length})`, "",
  "| Weapon | Skill | Why |\n| --- | --- | --- |",
  ...none.map(([s]) => `| ${s.weapon} | ${s.name}${s.ult ? " (ultimate)" : ""} | ${listOf([...s.reasons, ...s.unpriced])} |`),
  "",
].join("\n");
require("fs").writeFileSync(require("path").join(__dirname, "..", "..", "data", "skill-balance.md"), md);
console.log("Wrote data/skill-balance.md");
