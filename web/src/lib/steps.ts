import { tick } from "svelte";

type Kept = Record<string, unknown> | null;

/**
 * A step inside a screen, such as an action on a tray or a step of New batch, kept in the browser's
 * history so the phone's Back button undoes the step rather than leaving the screen.
 */
export function pushStep(name: string, value: unknown) {
  history.pushState({ ...((history.state as Kept) ?? {}), [name]: value }, "");
}

export function stepOf<T>(name: string): T | null {
  return (((history.state as Kept) ?? {})[name] ?? null) as T | null;
}

/** Calls back when Back or Forward moves between steps; returns how to stop listening. */
export function onStepChange(handler: () => void): () => void {
  window.addEventListener("popstate", handler);
  return () => window.removeEventListener("popstate", handler);
}

/** Moves focus to the new screen's heading, so keyboard and screen-reader users start there. */
export async function focusHeading() {
  await tick();
  const heading = document.querySelector<HTMLElement>("main h1");

  if (heading) {
    heading.setAttribute("tabindex", "-1");
    heading.focus({ preventScroll: true });
  }
}
