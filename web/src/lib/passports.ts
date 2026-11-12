/** Why a passport wasn't issued, in words, from the server's answer. */
export function passportProblems(status: number, problem?: string): string {
  switch (problem) {
    case "NotExamined":
      return "Examine the plants first, and tick to say so.";
    case "TooMany":
      return "There aren't that many in this tray now. Check the number.";
    case "CannotIssue":
      return "Something stops this passport being issued. Open it again to see what.";
    default:
      return status === 400
        ? "Please say where the plants are going."
        : "That couldn't be issued. Please try again.";
  }
}
