import type { components } from "../api/schema";

export type Schemas = components["schemas"];

export const unreachable =
  "This device can't reach Hortiplanner. Please check the connection and try again.";

/** Runs an action, turning a lost connection into a message for the screen. */
export async function attempt(
  action: () => Promise<void>,
  setProblem: (message: string) => void,
) {
  setProblem("");

  try {
    await action();
  } catch {
    setProblem(unreachable);
  }
}

/** Empty boxes mean "not given". */
export function optional(value: string): string | null {
  return value.trim() === "" ? null : value.trim();
}
