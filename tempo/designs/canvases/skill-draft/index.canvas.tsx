import type { TempoPage, TempoStoryboard, TempoRouteStoryboard } from 'tempo-sdk';
import { SkillDraftPage } from '@/pages/SkillDraftPage';
import { BuildPanel as BuildPanel2 } from '@/skill-draft/BuildPanel';
import { SkillCard } from '@/skill-draft/SkillCard';
import { SkillTree as SkillTree2 } from '@/skill-draft/SkillTree';
import { SKILLS, makeOwned } from '@/skill-draft/graph';

const page: TempoPage = {
  name: "Skill Draft",
};

export default page;

// Tree snapshot deep in a run: the player dove Novice → Ember → Magma (⇒ Pyromancer)
// → Conflagration, is now offered {Meteor Storm, Comet}, and has permanently MISSED
// {Slash, Guard, Soulflame}. Soulflame was the fork toward the Lich prestige class,
// so that entire sub-tree is now pruned — the path has shifted toward Archmage.
const TREE_OWNED = makeOwned("novice", "ember", "magma", "pyromancer", "conflagration");
const TREE_AVAILABLE = makeOwned("meteor", "comet");
const TREE_BURNED = makeOwned("slash", "guard", "soulflame");

// Build-panel snapshot after the path shifted all the way to a prestige class:
// Pyromancer ▸ Archmage (classes auto-revealed, excluded from the skill chips).
const CLASS_BUILD = makeOwned(
  "novice",
  "ember",
  "magma",
  "pyromancer",
  "conflagration",
  "meteor",
  "archmage",
);

// The sub-components are designed for the app's dark theme and render on a
// transparent surface; this backdrop mirrors the page so they stay legible
// in their canvas frames (the full-screen storyboard carries its own bg).
function Backdrop({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen text-[#e7ebf3] font-sans p-6" style={{ background: "#0a0c12" }}>
      {children}
    </div>
  );
}

export const SkillDraft: TempoStoryboard = {
  render: () => <SkillDraftPage />,
  name: "Level-Up Skill Draft (full screen)",
  layout: { x: 0, y: 0, width: 1280, height: 1750 },
};

export const GuaranteedCard: TempoStoryboard = {
  render: () => (
    <Backdrop>
      <SkillCard skill={SKILLS.magma} variant="guaranteed" onPick={() => {}} />
    </Backdrop>
  ),
  name: "Skill Card — Guaranteed",
  layout: { x: 1330, y: 0, width: 360, height: 440 },
};

export const WildcardCard: TempoStoryboard = {
  render: () => (
    <Backdrop>
      <SkillCard skill={SKILLS.comet} variant="wildcard" onPick={() => {}} />
    </Backdrop>
  ),
  name: "Skill Card — Wildcard",
  layout: { x: 1740, y: 0, width: 360, height: 440 },
};

export const SkillTree: TempoStoryboard = {
  render: () => (
    <Backdrop>
      <SkillTree2
        owned={TREE_OWNED}
        lastPicked="conflagration"
        available={TREE_AVAILABLE}
        burned={TREE_BURNED}
      />
    </Backdrop>
  ),
  name: "Skill Tree (6 paths — deep run shifted to Archmage)",
  layout: { x: 0, y: 1850, width: 1500, height: 1050 },
};

export const BuildPanel: TempoStoryboard = {
  render: () => (
    <Backdrop>
      <BuildPanel2 owned={CLASS_BUILD} level={4} />
    </Backdrop>
  ),
  name: "Build Panel (mid-run)",
  layout: { x: 2150, y: 0, width: 340, height: 320 },
};
