/** Why a passport wasn't issued, in words, from the server's answer. */
export function passportProblems(status: number, problem?: string): string {
  switch (problem) {
    case "NotExamined":
      return "Examine the plants first, and tick to say so.";
    case "TooMany":
      return "There aren't that many in this tray now. Check the number.";
    case "Changed":
      return "Part D has changed since this was opened, so nothing was issued. Check the passport above, then issue it.";
    case "CannotIssue":
      return "Something stops this passport being issued. Open it again to see what.";
    default:
      return status === 400
        ? "Please say where the plants are going, in under 200 letters."
        : "That couldn't be issued. Please try again.";
  }
}

/** What's still needed before a passport can be issued, or empty when it's ready. */
export function issueHint(state: {
  examined: boolean;
  issuedTo: string;
  quantity: number;
  available: number;
  blocked: boolean;
}): string {
  if (state.blocked) {
    return "Sort out what's missing above first.";
  }

  const needed = [
    state.issuedTo.trim() ? "" : "say where the plants are going",
    state.examined ? "" : "tick that you've examined them",
  ].filter(Boolean);

  if (needed.length > 0) {
    const words = needed.join(" and ");
    return `To issue it, ${words}.`;
  }

  return state.quantity < 1 || state.quantity > state.available
    ? `It can cover between 1 and ${state.available} plants.`
    : "";
}
