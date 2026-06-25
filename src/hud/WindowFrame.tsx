import type { ReactNode } from "react";

/**
 * The shared window chrome — stands in for the runtime `DraggableWindow` base
 * (title-bar drag · remember position · bring-to-front). Here it's the visual
 * shell only: title bar + close button + RO-style panel border.
 */
export function WindowFrame({
  title,
  icon,
  width,
  children,
  footer,
}: {
  title: string;
  icon?: string;
  width?: number;
  children: ReactNode;
  footer?: ReactNode;
}) {
  return (
    <div
      className="rounded-xl border border-[#2b3142] overflow-hidden font-sans text-[#e7ebf3] select-none"
      style={{ width, background: "linear-gradient(#141a28,#10141e)", boxShadow: "0 24px 60px rgba(0,0,0,.55)" }}
    >
      {/* Title bar — the only drag handle at runtime */}
      <div
        className="flex items-center justify-between px-3 py-2 border-b border-[#2b3650] cursor-move"
        style={{ background: "linear-gradient(#1c2333,#161d2c)" }}
      >
        <div className="flex items-center gap-2 text-[12px] font-bold tracking-wide uppercase text-[#cdd6ea]">
          {icon && <span className="text-[14px]">{icon}</span>}
          {title}
        </div>
        <button
          className="w-5 h-5 grid place-items-center rounded-md text-[#8a93ab] hover:text-white hover:bg-white/10 text-[12px] leading-none cursor-pointer"
          aria-label="Close"
        >
          ✕
        </button>
      </div>

      <div className="p-3">{children}</div>

      {footer && (
        <div className="px-3 py-2 border-t border-[#232b3d] text-[12px] text-[#97a0b5] bg-black/20">
          {footer}
        </div>
      )}
    </div>
  );
}
