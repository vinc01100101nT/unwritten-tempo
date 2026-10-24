// The skill balance formula — STEP 1, the simple version. The ONE copy: the report (report.js) and the calculator
// page (calculator.html) both load this file. The full system that replaces it is planned in
// docs/2-systems/active-skill-system-v3/skill-balancing-system-plan.md and waits on play-test calibration.
//
// Everything is counted in basic attacks (1.0 = one basic attack).
//   Budget = rate × cooldown + mana rate × MP + (cast + the longer of release lock and held run) ÷ swing
//   Value  = Σ damage × hits × chance × repeats × copies × targets × range × release-early
//          + Σ effects
// A passive is priced per second of fighting instead: one firing's worth × how often its moment comes.
// Every constant is a proposal, not a measurement — play-testing is what corrects them. The ones in the second group
// are GUESSES about how a fight goes; a skill priced with any of them is "provisional".
var BALANCE = {
  rate: 0.1,          // per second of cooldown
  ultRate: 0.075,     // per second of an ultimate's cooldown
  manaRate: 0.0625,   // per MP (an 8 MP, 5 s skill = 1.0)
  ranged: 0.85,       // a hit that lands 8 m or more away
  enemyRate: 1.0,     // enemy attacks per second
  releaseEarly: 1.5,  // a wind-up held to the end (StrikeMath.ReleaseScaleAtFull)
  physicalShare: 0.5, // share of enemy blows that are physical (defence) vs magic (magic defence)
  passiveRate: 0.1,   // a passive's budget per second of fighting = what a second of an active's cooldown buys
  tolerance: 15,      // % — balanced
  shortlist: 40,      // % — far enough off to play-test first
  // ---- provisional: guesses about a fight, until play-testing settles them
  positioning: 0.5,   // share of a fight where being in the right place matters (slows, speed, dashes)
  runSpeed: 2.0,      // metres a second a person runs (CharacterSpec.FullSpeed, read in the editor 2026-10-24)
  blowShare: 0.05,    // share of your max HP one enemy blow takes
  missRate: 0.2,      // share of ordinary attacks that miss
  markBonus: 0.4,     // what cashing one mark adds (the combinations' usual bonus per mark)
  markCash: 0.5,      // share of placed marks that get cashed in
  killEvery: 10,      // seconds between kills
  dodgeShare: 0.2,    // share of enemy blows you dodge
  lowHpEvery: 30,     // seconds between drops to low HP
  disableEvery: 6,    // seconds between disables landing on you
  disableSecs: 2,     // how long one disable lasts
  refAttack: 53,      // attack that flat HP numbers (burn, regen) are measured against — level 50, before gear
  burnLeftHp: 12,     // HP a burn still has left when it is put out
  cleanseWorth: 0.5,  // ailments one cleanse saves you, in basic attacks
  tauntPerSec: 0.32,  // a second of taunt (Howling Wind's own budget ÷ its 5 s)
};

// The skills one skill is priced against (a reset worth another skill's budget). The page and the report set it.
var BALANCE_POOL = typeof SKILL_DATA !== "undefined" ? SKILL_DATA : [];

function balanceEffectValue(f, swing, depth) {
  const B = BALANCE;
  const find = id => BALANCE_POOL.find(x => x.id === id);
  switch (f.type) {
    case "count": return f.count * f.size * f.targets;                       // hits stopped / attacks boosted
    case "nextHit": return f.size * f.count * f.targets;                     // enemy's next attacks cut by size
    case "enemyTimed": return f.size * f.secs * B.enemyRate * f.targets;
    case "ownSpeed": return f.size * f.secs / swing;
    // Defence is a cut of every blow of its kind, 0.01 = 1% (UnitStatMath hard defence); a blow is either physical or
    // magic, so each side counts only its share of the enemy's blows.
    case "ownDef": return f.size * f.secs * B.enemyRate * (/Magic/.test(f.label) ? 1 - B.physicalShare : B.physicalShare);
    case "enemyDef": return f.size * f.secs / swing * f.targets;   // your blows on them land harder
    case "mana": return f.count * B.manaRate * f.targets;
    case "mpRegen": return f.size * f.secs * B.manaRate;
    // provisional
    case "move": return f.size * f.secs * B.positioning / swing * f.targets;
    case "dash": return f.count * f.size / B.runSpeed * B.positioning / swing;   // size = metres covered
    case "healShare": return f.size / B.blowShare;   // a heal of 10% of max HP undoes two enemy blows
    case "accSelf": return Math.min(f.size, B.missRate) * f.secs / swing;
    case "evaSelf": return f.size * f.secs * B.enemyRate;
    case "taunt": return f.secs * B.tauntPerSec * f.targets;
    case "cleanse": return B.cleanseWorth;
    case "mark": return f.count * B.markBonus * B.markCash * f.targets;
    case "extendMarks": return f.count * B.markBonus * B.markCash * f.targets * 0.25;   // one of four mark kinds
    case "flatHp": return f.size / B.refAttack * f.targets;
    case "burnPayoff": return f.size * B.burnLeftHp / B.refAttack * f.targets;
    case "immune": return f.secs / B.disableEvery * B.disableSecs * B.enemyRate;
    case "sureHit": return f.count * B.missRate;
    case "refBudget": { const t = find(f.skillId); return t ? f.size * balanceEvaluate(t, depth + 1).budget : 0; }
    case "refValue": { const t = find(f.skillId); return t ? f.size * balanceEvaluate(t, depth + 1).perFire : 0; }
    case "ultAvg": {
      const ults = BALANCE_POOL.filter(x => x.weapon === f.weapon && x.ult);
      return ults.length ? f.size * ults.reduce((a, x) => a + balanceEvaluate(x, depth + 1).budget, 0) / ults.length : 0;
    }
    case "companionDodge": {    // one counter per dodge, for the window: the base dodge share plus the window's evasion
      const t = find(f.skillId);
      return t ? balanceEvaluate(t, depth + 1).perFire * (B.dodgeShare + f.size) * f.secs * B.enemyRate : 0;
    }
    default: return 0;
  }
}

function balanceFireRate(fr) {
  if (!fr) return 0;
  const B = BALANCE;
  switch (fr.kind) {
    case "cooldown": return 1 / fr.secs;
    case "enemy": return B.enemyRate;
    case "dodge": return B.enemyRate * B.dodgeShare;
    case "kill": return 1 / B.killEvery;
    case "killDuring": return (1 / B.killEvery) * (fr.window / fr.cd);
    case "lowHp": return 1 / B.lowHpEvery;
    case "parry": return 1 / fr.secs;
    case "duringRun": {   // once per hit the run lands: its beats × the enemies each beat reaches, per cooldown
      const t = BALANCE_POOL.find(x => x.id === fr.skillId);
      if (!t) return 0;
      const reach = Math.max(1, ...t.parts.map(p => p.targets));
      return t.repeats * reach / fr.cd;
    }
    default: return 0;
  }
}
const BALANCE_PROVISIONAL_RATES = ["dodge", "kill", "killDuring", "lowHp", "parry", "duringRun"];

function balanceEvaluate(s, depth) {
  depth = depth || 0;
  const rows = [];
  if (s.reasons.length || !s.swing || depth > 3) return { status: "not priced", rows, budget: 0, value: 0, perFire: 0, off: NaN };
  const rate = s.ult ? BALANCE.ultRate : BALANCE.rate;
  const budget = s.passive ? BALANCE.passiveRate
    : rate * s.cd + BALANCE.manaRate * s.mp + (s.cast + Math.max(s.lock, s.run)) / s.swing;
  let value = 0, prov = false;
  s.parts.forEach(p => {
    const v = p.dmg * p.hits * p.chance * s.repeats * p.copies * p.targets *
              (p.ranged ? BALANCE.ranged : 1) * (p.releaseEarly ? BALANCE.releaseEarly : 1);
    value += v; rows.push([p.label, v]); prov = prov || !!p.prov;
  });
  s.effects.forEach(f => {
    const v = balanceEffectValue(f, s.swing, depth); value += v; rows.push([f.label, v]); prov = prov || !!f.prov;
  });
  const perFire = value;
  if (s.passive) {
    const fires = balanceFireRate(s.fireRate);
    value *= fires; rows.forEach(r => r[1] *= fires);
    prov = prov || BALANCE_PROVISIONAL_RATES.includes(s.fireRate && s.fireRate.kind);
  }
  const off = budget > 0 ? (value - budget) / budget * 100 : NaN;
  const status = value === 0 && s.unpriced.length ? "not priced"
    : s.unpriced.length ? "partly priced" : prov ? "provisional" : "priced";
  return { status, rows, budget, value, perFire, off };
}

if (typeof module !== "undefined") module.exports = { BALANCE, balanceEvaluate, setPool: p => { BALANCE_POOL = p; } };
