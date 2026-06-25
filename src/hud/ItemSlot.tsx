import { ITEMS, RARITY, type EquipmentInstance, type InvStack } from "./items";

/**
 * The ONE slot widget — one visual language everywhere (inventory grid,
 * equipment paperdoll, card-insert dialog). It renders four INDEPENDENT
 * channels so they never compete:
 *   • border color = rarity
 *   • +N badge     = enhancement (top-left)
 *   • ◇ pips       = card slots (bottom, neutral)
 *   • icon         = identity (untouched)
 */
export function ItemSlot({
  stack,
  size = 52,
  selected = false,
  emptyGlyph,
  label,
  onClick,
  onHover,
}: {
  stack?: InvStack | null;
  size?: number;
  selected?: boolean;
  emptyGlyph?: string;
  label?: string;
  onClick?: () => void;
  onHover?: (hovering: boolean) => void;
}) {
  const def = stack ? ITEMS[stack.defId] : undefined;
  const rarity = def ? RARITY[def.rarity] : undefined;
  const inst: EquipmentInstance | undefined = stack?.instance;

  const border = rarity?.border ?? "#2b3142";
  const cardSlots = def?.cardSlots ?? 0;
  const cardsUsed = inst?.cards.length ?? 0;

  return (
    <button
      onClick={onClick}
      onMouseEnter={() => onHover?.(true)}
      onMouseLeave={() => onHover?.(false)}
      title={label}
      className="relative grid place-items-center rounded-lg cursor-pointer transition-transform duration-100 hover:-translate-y-0.5"
      style={{
        width: size,
        height: size,
        background: def ? "radial-gradient(circle at 50% 25%, #1b2233, #0e121c)" : "rgba(255,255,255,.02)",
        border: `2px solid ${border}`,
        boxShadow: selected
          ? `0 0 0 2px #fff6, 0 0 18px ${rarity?.glow ?? "rgba(255,255,255,.15)"}`
          : rarity && rarity.glow !== "transparent"
          ? `0 0 14px ${rarity.glow}`
          : "none",
      }}
    >
      {def ? (
        <span className="text-[26px] leading-none" style={{ filter: "drop-shadow(0 1px 1px rgba(0,0,0,.6))" }}>
          {def.icon}
        </span>
      ) : (
        <span className="text-[20px] opacity-25">{emptyGlyph ?? ""}</span>
      )}

      {/* Enhancement — its own channel, never recolors the border */}
      {inst && inst.upgrade > 0 && (
        <span className="absolute top-0.5 left-0.5 text-[10px] font-extrabold text-[#ffe08a] leading-none drop-shadow-[0_1px_1px_rgba(0,0,0,.9)]">
          +{inst.upgrade}
        </span>
      )}

      {/* Stack count */}
      {stack && stack.amount > 1 && (
        <span className="absolute bottom-0.5 right-1 text-[11px] font-bold text-white leading-none drop-shadow-[0_1px_1px_rgba(0,0,0,.9)]">
          {stack.amount}
        </span>
      )}

      {/* New marker */}
      {stack?.isNew && (
        <span className="absolute -top-1 -right-1 w-2 h-2 rounded-full bg-[#46d17f] shadow-[0_0_6px_#46d17f]" />
      )}

      {/* Card pips — always neutral, never rarity-tinted */}
      {cardSlots > 0 && (
        <span className="absolute bottom-0.5 left-1 flex gap-[1px] text-[8px] leading-none text-[#9aa3b8]">
          {Array.from({ length: cardSlots }).map((_, i) => (
            <span key={i}>{i < cardsUsed ? "◇" : "□"}</span>
          ))}
        </span>
      )}
    </button>
  );
}
