import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./globals.css";
import TempoHost from "./.tempo/tempo-host";

// This is the Tempo canvas dev server ("tempo-host"). It exists only to render
// your real components onto the design canvas — it is NOT your app. Build your
// actual app OUTSIDE the tempo/ folder, at the project root (e.g. src/...).
createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <TempoHost />
  </StrictMode>,
);


