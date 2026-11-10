import type { Schemas } from "./types";

export type Place = Schemas["LocationResponse"];
export type PlaceKind = Schemas["LocationKind"];

export const placeKinds: { kind: PlaceKind; label: string }[] = [
  { kind: "Site", label: "Site" },
  { kind: "Greenhouse", label: "Greenhouse" },
  { kind: "Polytunnel", label: "Polytunnel" },
  { kind: "Bench", label: "Bench" },
  { kind: "Bay", label: "Bay" },
  { kind: "StandingBed", label: "Standing bed" },
  { kind: "ColdFrame", label: "Cold frame" },
  { kind: "SalesArea", label: "Sales area" },
  { kind: "Area", label: "Area" },
];

export function kindLabel(kind: PlaceKind): string {
  return placeKinds.find((item) => item.kind === kind)?.label ?? kind;
}

/** Codes printed on place labels look like P0001; tray labels start with C. */
export function isPlaceCode(code: string): boolean {
  return /^P\d{4,}$/iu.test(code.trim());
}

export function findPlaceByCode(
  code: string,
  places: Place[],
): Place | undefined {
  const wanted = code.trim().toUpperCase();
  return places.find((place) => place.labelCode.toUpperCase() === wanted);
}

/** The place and everything inside it, however deep. */
export function withEverythingInside(id: string, places: Place[]): Set<string> {
  const found = new Set([id]);
  let grew = true;

  while (grew) {
    grew = false;

    for (const place of places) {
      if (place.parentId && found.has(place.parentId) && !found.has(place.id)) {
        found.add(place.id);
        grew = true;
      }
    }
  }

  return found;
}

/** Where a place could be moved to: anywhere not inside itself. */
export function possibleParents(id: string, places: Place[]): Place[] {
  const inside = withEverythingInside(id, places);
  return places
    .filter((place) => !inside.has(place.id) && !place.archived)
    .sort((a, b) => a.path.localeCompare(b.path, "en-GB", { numeric: true }));
}

/** Places a tray can stand in, by their full names: the ones in use first, then in number order. */
export function trayPlaces(places: Place[]): { id: string; label: string }[] {
  return places
    .filter((place) => place.kind !== "Site" && !place.archived)
    .sort(
      (a, b) =>
        (b.lastUsedAt ?? "").localeCompare(a.lastUsedAt ?? "") ||
        (a.path || a.name).localeCompare(b.path || b.name, "en-GB", {
          numeric: true,
        }),
    )
    .map((place) => ({ id: place.id, label: place.path || place.name }));
}

/** Production rows gathered by tray, as one tray can hold several batches. */
export function byContainer<T extends { containerId: string }>(
  rows: T[],
): T[][] {
  const trays = new Map<string, T[]>();

  for (const row of rows) {
    trays.set(row.containerId, [...(trays.get(row.containerId) ?? []), row]);
  }

  return [...trays.values()];
}
