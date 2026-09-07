import { useSyncExternalStore } from "react";

/**
 * Which layout the viewport is in.
 *
 * The breakpoints are the ones the design brief fixes (375 / 768 / 1024 / 1440)
 * collapsed to the three layouts the app actually has: a single column, a
 * two-column split, and the full three-column dashboard.
 */
export type Breakpoint = "mobile" | "tablet" | "desktop";

const QUERIES: [Breakpoint, string][] = [
  ["mobile", "(max-width: 767px)"],
  ["tablet", "(min-width: 768px) and (max-width: 1279px)"],
  ["desktop", "(min-width: 1280px)"],
];

/**
 * Subscribes to `matchMedia` rather than to resize.
 *
 * A resize listener fires on every pixel of a drag and forces a render each
 * time; a media query fires only when the answer actually changes, which is the
 * only moment the layout has anything to do. It also matches what CSS is doing,
 * so the JS and the stylesheet can never disagree about which layout is active.
 */
function subscribe(callback: () => void): () => void {
  if (typeof window === "undefined" || !window.matchMedia) return () => undefined;

  const lists = QUERIES.map(([, query]) => window.matchMedia(query));
  for (const list of lists) list.addEventListener("change", callback);

  return () => {
    for (const list of lists) list.removeEventListener("change", callback);
  };
}

function read(): Breakpoint {
  if (typeof window === "undefined" || !window.matchMedia) return "desktop";

  for (const [name, query] of QUERIES) {
    if (window.matchMedia(query).matches) return name;
  }
  return "desktop";
}

export function useBreakpoint(): Breakpoint {
  // The server snapshot is "desktop" because there is no viewport to measure
  // during prerender; the first client render corrects it.
  return useSyncExternalStore(subscribe, read, () => "desktop" as const);
}

/** Convenience for the common "is this a small screen" branch. */
export function useIsCompact(): boolean {
  return useBreakpoint() !== "desktop";
}

/** True when the user has asked the system to reduce motion. */
export function usePrefersReducedMotion(): boolean {
  return useSyncExternalStore(
    (callback) => {
      if (typeof window === "undefined" || !window.matchMedia) return () => undefined;
      const list = window.matchMedia("(prefers-reduced-motion: reduce)");
      list.addEventListener("change", callback);
      return () => list.removeEventListener("change", callback);
    },
    () => window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false,
    () => false,
  );
}
