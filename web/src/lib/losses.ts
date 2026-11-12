/** Plants lost for a reason, with trays lost before anyone counted them told apart, as they have no number. */
export function lossWords(plants: number, uncountedTrays: number): string {
  if (uncountedTrays === 0) {
    return String(plants);
  }

  const trays = `${uncountedTrays} uncounted tray${uncountedTrays === 1 ? "" : "s"}`;
  return plants > 0 ? `${plants} + ${trays}` : trays;
}
