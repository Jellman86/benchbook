import { getContext, setContext, untrack } from "svelte";

// The breadcrumb trail above every screen. Each screen adds its own part while it's shown, below the screens it sits
// in, so the trail always reads from the top level down to where the person is.
export type Crumb = { label: string; go?: () => void };

type Part = { id: number; depth: number; crumbs: Crumb[] };

const depthKey = Symbol("crumb depth");
const parts = $state<Part[]>([]);
let nextId = 0;

/**
 * Adds this screen's crumbs to the trail for as long as it's shown. Call it while the component starts; `get` is
 * re-read whenever what it uses changes. A crumb with `go` takes the person back to that level.
 */
export function crumbs(get: () => Crumb[]) {
  const depth = (getContext<number | undefined>(depthKey) ?? -1) + 1;
  setContext(depthKey, depth);
  const id = nextId++;

  $effect(() => {
    // Only what the screen's crumbs depend on is tracked; the trail itself is written untracked, or each write would
    // re-run this effect.
    const part = { id, depth, crumbs: get() };

    untrack(() => {
      const index = parts.findIndex((item) => item.id === id);

      if (index >= 0) {
        parts[index] = part;
      } else {
        parts.push(part);
      }
    });

    return () =>
      untrack(() => {
        const at = parts.findIndex((item) => item.id === id);

        if (at >= 0) {
          parts.splice(at, 1);
        }
      });
  });
}

/** The whole trail, top level first. */
export const trail = (): Crumb[] =>
  [...parts]
    .sort((a, b) => a.depth - b.depth || a.id - b.id)
    .flatMap((part) => part.crumbs);
