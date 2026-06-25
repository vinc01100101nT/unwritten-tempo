import { useEffect, useState } from "react";
import { SkillDraftPage } from "./pages/SkillDraftPage";
import { HudShowcasePage } from "./pages/HudShowcasePage";

type View = "hud" | "skills";

/** Minimal hash-based switcher so the prototype is browsable at `npm run dev`
 *  ( #/hud · #/skills ) without pulling in a router. */
function useHashView(): View {
  const read = (): View => (window.location.hash.includes("skills") ? "skills" : "hud");
  const [view, setView] = useState<View>(read);
  useEffect(() => {
    const on = () => setView(read());
    window.addEventListener("hashchange", on);
    return () => window.removeEventListener("hashchange", on);
  }, []);
  return view;
}

export default function App() {
  const view = useHashView();
  return (
    <div className="min-h-screen" style={{ background: "#0a0c12" }}>
      <nav className="flex items-center gap-1 px-4 py-2 border-b border-[#1c2333] font-sans text-[13px] sticky top-0 z-50" style={{ background: "rgba(10,12,18,.85)", backdropFilter: "blur(6px)" }}>
        <span className="text-[#cdd6ea] font-bold mr-3">unwritten</span>
        <NavLink href="#/hud" active={view === "hud"}>HUD Windows</NavLink>
        <NavLink href="#/skills" active={view === "skills"}>Skill Draft</NavLink>
      </nav>
      {view === "hud" ? <HudShowcasePage /> : <SkillDraftPage />}
    </div>
  );
}

function NavLink({ href, active, children }: { href: string; active: boolean; children: React.ReactNode }) {
  return (
    <a
      href={href}
      className="px-3 py-1 rounded-md transition-colors"
      style={
        active
          ? { background: "#2a3550", color: "#fff", border: "1px solid #46557f" }
          : { color: "#8a93ab", border: "1px solid transparent" }
      }
    >
      {children}
    </a>
  );
}
