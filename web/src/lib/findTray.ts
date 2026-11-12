import type { Schemas } from "./types";

type Container = Schemas["ContainerResponse"];

const mostShown = 20;

const wordsIn = (text: string) =>
  text
    .toLowerCase()
    .split(/[^\p{L}\p{N}]+/u)
    .filter(Boolean);

/**
 * Trays whose plant, variety, place, tray code or batch code have a word starting with each word typed, so they can be opened
 * without a label. Closer matches first: a code or plant name that starts with what was typed.
 */
export function findTrays(containers: Container[], query: string): Container[] {
  const words = wordsIn(query);

  if (words.length === 0) {
    return [];
  }

  const text = (container: Container) =>
    [
      container.labelCode,
      container.locationName ?? "",
      container.potSizeName ?? "",
      ...container.contents.flatMap((line) => [
        line.commonName,
        line.variety ?? "",
        line.traceabilityCode,
      ]),
    ].join(" ");

  const startsWell = (container: Container) =>
    [
      container.labelCode,
      ...container.contents.map((line) => line.commonName),
    ].some((name) => name.toLowerCase().startsWith(words[0]));

  return containers
    .filter((container) => {
      // Each typed word must begin a word of the tray's, so a single letter doesn't match everything.
      const known = wordsIn(text(container));
      return words.every((word) => known.some((each) => each.startsWith(word)));
    })
    .sort(
      (a, b) =>
        Number(startsWell(b)) - Number(startsWell(a)) ||
        a.labelCode.localeCompare(b.labelCode),
    )
    .slice(0, mostShown);
}
