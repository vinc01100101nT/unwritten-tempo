import type { TempoPage, TempoStoryboard, TempoRouteStoryboard } from 'tempo-sdk';
import { CharacterWindow } from '@/hud/CharacterWindow';
import { EquipmentWindow } from '@/hud/EquipmentWindow';
import { InventoryWindow } from '@/hud/InventoryWindow';
import { HudShowcasePage } from '@/pages/HudShowcasePage';

const page: TempoPage = {
  name: "HUD Windows",
};

export default page;

// The individual windows render on a transparent surface; this backdrop mirrors
// the in-game dark theme so they stay legible in their own canvas frames (the
// full-screen Showcase storyboard carries its own background).
function Backdrop({ children }: { children: React.ReactNode }) {
  return (
    <div
      className="min-h-screen font-sans p-8"
      style={{ background: "radial-gradient(900px 500px at 60% -10%, #1a2336 0%, #0a0c12 55%), #0a0c12" }}
    >
      {children}
    </div>
  );
}

export const Showcase: TempoStoryboard = {
  render: () => <HudShowcasePage />,
  name: "Progression Windows — full picture",
  layout: { x: 0, y: 0, width: 1500, height: 1040 },
};

export const Inventory: TempoStoryboard = {
  render: () => (
    <Backdrop>
      <InventoryWindow />
    </Backdrop>
  ),
  name: "Inventory (hover = stat-delta preview)",
  layout: { x: 1550, y: 0, width: 560, height: 560 },
};

export const Equipment: TempoStoryboard = {
  render: () => (
    <Backdrop>
      <EquipmentWindow />
    </Backdrop>
  ),
  name: "Equipment (paperdoll + cards)",
  layout: { x: 2160, y: 0, width: 380, height: 660 },
};

export const Character: TempoStoryboard = {
  render: () => (
    <Backdrop>
      <CharacterWindow />
    </Backdrop>
  ),
  name: "Character (read-only readout)",
  layout: { x: 2780, y: 0, width: 340, height: 520 },
};
