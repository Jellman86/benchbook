import type { PlaceKind } from "./places";

export type Point = [number, number];

// Bright enough to read over fields and roofs from above, whichever theme the screen is in.
const outlineColours: Record<PlaceKind, string> = {
  Site: "#ffffff",
  Greenhouse: "#8fb0ff",
  Polytunnel: "#c99cf0",
  Bench: "#ff9ccb",
  Bay: "#ff9ccb",
  ColdFrame: "#ff9ccb",
  StandingBed: "#c6eb7e",
  Area: "#c6eb7e",
  SalesArea: "#ffd479",
};

export function outlineStyle(kind: PlaceKind, chosen = false) {
  return {
    color: outlineColours[kind],
    weight: chosen ? 4 : 2,
    opacity: 1,
    dashArray: kind === "Site" ? "8 6" : undefined,
    fillColor: outlineColours[kind],
    fillOpacity: kind === "Site" ? 0.04 : chosen ? 0.35 : 0.18,
  };
}

/** Big places carry their name on the map; small ones show it when touched, so benches don't hide the picture. */
export const alwaysNamed = (kind: PlaceKind) =>
  !["Bench", "Bay", "ColdFrame"].includes(kind);

export type Imagery = {
  url: string;
  attribution: string;
  maxNativeZoom: number;
  satellite: boolean;
};

/** Esri's satellite pictures when the nursery has a key; otherwise OpenStreetMap, which needs none. */
export function imagery(key: string | null | undefined): Imagery {
  return key
    ? {
        url: `https://ibasemaps-api.arcgis.com/arcgis/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}?token=${encodeURIComponent(key)}`,
        attribution:
          'Powered by <a href="https://www.esri.com" target="_blank" rel="noopener">Esri</a> | Esri, Maxar, Earthstar Geographics, and the GIS User Community',
        maxNativeZoom: 19,
        satellite: true,
      }
    : {
        url: "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        attribution:
          '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a> contributors',
        maxNativeZoom: 19,
        satellite: false,
      };
}

/** A UK postcode in an address, such as "DE4 2AB", or null. */
export function postcodeIn(address: string | null | undefined): string | null {
  const match = /\b([A-Z]{1,2}\d[A-Z\d]?)\s*(\d[A-Z]{2})\b/i.exec(
    address ?? "",
  );
  return match ? `${match[1]} ${match[2]}`.toUpperCase() : null;
}

/** The corners as saved: [latitude, longitude], without the closing repeat of the first corner. */
export function corners(points: { lat: number; lng: number }[]): Point[] {
  const all = points.map((point): Point => [point.lat, point.lng]);
  const [first] = all;
  const last = all.at(-1);

  return all.length > 1 &&
    first &&
    last &&
    first[0] === last[0] &&
    first[1] === last[1]
    ? all.slice(0, -1)
    : all;
}

/**
 * Where an address is, from Esri's address search with the nursery's map key, or null if it isn't found or the key
 * can't search. The result only moves the map; it's never kept.
 */
export async function geocodeAddress(
  address: string,
  key: string,
): Promise<Point | null> {
  const query = new URLSearchParams({
    SingleLine: address,
    countryCode: "GBR",
    maxLocations: "1",
    forStorage: "false",
    f: "json",
    token: key,
  });

  try {
    const response = await fetch(
      `https://geocode-api.arcgis.com/arcgis/rest/services/World/GeocodeServer/findAddressCandidates?${query}`,
    );
    const found = response.ok
      ? (
          (await response.json()) as {
            candidates?: {
              location?: { x: number; y: number };
              score?: number;
            }[];
          }
        ).candidates?.[0]
      : undefined;

    return found?.location && (found.score ?? 0) >= 70
      ? [found.location.y, found.location.x]
      : null;
  } catch {
    return null;
  }
}
