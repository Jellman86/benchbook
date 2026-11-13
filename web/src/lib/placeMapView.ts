import type * as Leaflet from "leaflet";
import { imagery, type Point } from "./placeMap";

// Setting up the map places are drawn on: Esri's satellite pictures or a street map, in degrees, or the nursery's own
// picture, where points are kept as fractions of its height and width. Shared by the map step and a place's page.
export type MapSource = {
  imageryKey?: string | null;
  onPicture?: boolean;
  pictureChangedAt?: string | null;
};

export type PlaceMapView = {
  L: typeof Leaflet;
  map: Leaflet.Map;
  /** From what's kept to where it is on the screen's map. */
  toMap: (point: Point) => Point;
  /** From where it is on the screen's map to what's kept. */
  fromMap: (point: Point) => Point;
  onPicture: boolean;
  satellite: boolean;
};

/** Leaflet, with Geoman for drawing when asked for. Loaded only when a map is shown. */
export async function loadLeaflet(drawing: boolean): Promise<typeof Leaflet> {
  const leaflet = await import("leaflet");
  await import("leaflet/dist/leaflet.css");
  const L = leaflet.default;

  if (drawing) {
    // Geoman adds itself to the Leaflet it finds on the page.
    (window as unknown as { L: typeof Leaflet }).L = L;
    await import("@geoman-io/leaflet-geoman-free");
    await import("@geoman-io/leaflet-geoman-free/dist/leaflet-geoman.css");
  }

  return L;
}

export async function openPlaceMap(
  L: typeof Leaflet,
  into: HTMLElement,
  source: MapSource,
  options: { quiet?: boolean } = {},
): Promise<PlaceMapView> {
  if (source.onPicture && source.pictureChangedAt) {
    const url = `/api/settings/map/picture?v=${encodeURIComponent(source.pictureChangedAt)}`;
    const image = new Image();
    image.src = url;
    await image.decode();
    const height = image.naturalHeight;
    const width = image.naturalWidth;
    const bounds: Leaflet.LatLngBoundsExpression = [
      [0, 0],
      [height, width],
    ];
    const map = L.map(into, {
      crs: L.CRS.Simple,
      zoomControl: !options.quiet,
      minZoom: -6,
      maxZoom: 4,
      zoomSnap: 0.25,
      attributionControl: false,
    });
    L.imageOverlay(url, bounds).addTo(map);
    map.fitBounds(bounds);
    map.setMaxBounds(L.latLngBounds(bounds).pad(0.25));

    return {
      L,
      map,
      toMap: (point) => [height * (1 - point[0]), width * point[1]],
      fromMap: (point) => [1 - point[0] / height, point[1] / width],
      onPicture: true,
      satellite: false,
    };
  }

  const map = L.map(into, {
    zoomControl: !options.quiet,
    maxZoom: 22,
  }).setView([54.4, -2.6], 6);
  const tiles = imagery(source.imageryKey);
  L.tileLayer(tiles.url, {
    attribution: tiles.attribution,
    maxNativeZoom: tiles.maxNativeZoom,
    maxZoom: 22,
  })
    .addTo(map)
    .bringToBack();

  return {
    L,
    map,
    toMap: (point) => point,
    fromMap: (point) => point,
    onPicture: false,
    satellite: tiles.satellite,
  };
}
