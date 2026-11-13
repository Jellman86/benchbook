<script lang="ts">
  import type * as Leaflet from "leaflet";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import {
    alwaysNamed,
    corners,
    geocodeAddress,
    imagery,
    outlineStyle,
    postcodeIn,
    type Point,
  } from "../../lib/placeMap";
  import { kindLabel, type Place, type PlaceKind } from "../../lib/places";
  import { attempt } from "../../lib/types";

  // One place's outline on a satellite picture: find the address, then tap the place's corners. The places already
  // drawn show faintly around it, so a greenhouse can be put beside the others. Used when adding a place and from
  // the place itself.
  let {
    place,
    places,
    onSaved,
    onSkip,
    skipLabel = "Skip the map",
  }: {
    place: {
      id: string;
      name: string;
      kind: PlaceKind;
      parentId: string | null;
    };
    places: Place[];
    onSaved: () => void;
    onSkip: () => void;
    skipLabel?: string;
  } = $props();

  type Mode = "drawing" | "drawn" | "editing";

  let box = $state<HTMLDivElement | null>(null);
  let mode = $state<Mode>("drawing");
  let search = $state("");
  let problem = $state("");
  let busy = $state(false);
  let ready = $state(false);
  let satellite = $state(true);
  let key = $state<string | null>(null);

  let L: typeof Leaflet;
  let map: Leaflet.Map | null = null;
  let others: Leaflet.FeatureGroup | null = null;
  // Plain, not reactive: Leaflet owns it.
  let shape: Leaflet.Polygon | null = null;

  const setProblem = (message: string) => (problem = message);

  function startDrawing() {
    if (!map) {
      return;
    }

    shape?.remove();
    shape = null;
    mode = "drawing";
    problem = "";
    map.pm.enableDraw("Polygon", {
      allowSelfIntersection: false,
      snappable: true,
      snapDistance: 16,
      tooltips: false,
      templineStyle: { color: "#ffffff" },
      hintlineStyle: { color: "#ffffff", dashArray: "5 5" },
      pathOptions: outlineStyle(place.kind, true),
    });
  }

  function finishDrawing() {
    // Geoman finishes when the first corner is tapped again; this does the same from a button.
    const polygon = map?.pm.Draw.Polygon as unknown as {
      _finishShape?: () => void;
    };
    polygon._finishShape?.();
  }

  function startEditing() {
    if (!shape) {
      return;
    }

    mode = "editing";
    shape.pm.enable({ allowSelfIntersection: false, snappable: true });
  }

  const save = () =>
    attempt(async () => {
      if (!shape) {
        problem = "Tap the place's corners first.";
        return;
      }

      shape.pm.disable();
      const points = corners(
        (shape.getLatLngs() as Leaflet.LatLng[][])[0] ?? [],
      );
      busy = true;

      try {
        const { response } = await api.PUT("/api/locations/{id}/outline", {
          params: { path: { id: place.id } },
          body: { points },
        });

        if (response.ok) {
          onSaved();
        } else {
          mode = "drawn";
          problem =
            "That outline couldn't be saved. It needs at least three corners.";
        }
      } finally {
        busy = false;
      }
    }, setProblem);

  const find = (event?: SubmitEvent) => {
    event?.preventDefault();

    return attempt(async () => {
      const wanted = search.trim();

      if (!wanted || !map) {
        return;
      }

      problem = "";
      const code = postcodeIn(wanted);
      let found: Point | null = null;

      // A whole address needs the map key's address search; a postcode alone works without one.
      if (key && (!code || wanted.length > code.length + 2)) {
        found = await geocodeAddress(wanted, key);
      }

      if (!found && code) {
        // postcodes.io is free and open (Open Government Licence), and only told the postcode.
        const response = await fetch(
          `https://api.postcodes.io/postcodes/${encodeURIComponent(code)}`,
        );
        const result = response.ok
          ? (
              (await response.json()) as {
                result?: { latitude: number; longitude: number };
              }
            ).result
          : undefined;
        found = result ? [result.latitude, result.longitude] : null;
      }

      if (found) {
        map.setView(found, 19);
      } else {
        problem = key
          ? `Couldn't find "${wanted}". Try the postcode, or move the map by hand.`
          : `Couldn't find "${wanted}". Try a postcode, or move the map by hand.`;
      }
    }, setProblem);
  };

  function whereIAm() {
    map?.locate({ setView: true, maxZoom: 19, enableHighAccuracy: true });
  }

  /** The places already drawn, faint and untappable, for something to line the new one up against. */
  function drawOthers(outlines: Record<string, Point[]>) {
    for (const other of places) {
      const points = outlines[other.id];

      if (!points || other.id === place.id) {
        continue;
      }

      const layer = L.polygon(points, {
        ...outlineStyle(other.kind),
        interactive: false,
        fillOpacity: 0.08,
      });
      // Names go in as text, never as markup.
      const label = document.createElement("span");
      label.textContent = other.name;
      layer.bindTooltip(label, {
        permanent: alwaysNamed(other.kind),
        direction: "center",
        className: "place-map-label",
      });
      layer.addTo(others!);
    }
  }

  onMount(() => {
    let stopped = false;

    void attempt(async () => {
      const leaflet = await import("leaflet");
      await import("leaflet/dist/leaflet.css");
      L = leaflet.default;
      // Geoman adds itself to the Leaflet it finds on the page.
      (window as unknown as { L: typeof Leaflet }).L = L;
      await import("@geoman-io/leaflet-geoman-free");
      await import("@geoman-io/leaflet-geoman-free/dist/leaflet-geoman.css");

      if (stopped || !box) {
        return;
      }

      map = L.map(box, { zoomControl: true, maxZoom: 22 }).setView(
        [54.4, -2.6],
        6,
      );
      others = L.featureGroup().addTo(map);

      const { data } = await api.GET("/api/locations/map");
      const outlines: Record<string, Point[]> = Object.fromEntries(
        (data?.outlines ?? []).map((outline) => [
          outline.id,
          outline.points as Point[],
        ]),
      );
      key = data?.imageryKey ?? null;
      const tiles = imagery(key);
      satellite = tiles.satellite;
      L.tileLayer(tiles.url, {
        attribution: tiles.attribution,
        maxNativeZoom: tiles.maxNativeZoom,
        maxZoom: 22,
      })
        .addTo(map)
        .bringToBack();
      drawOthers(outlines);

      map.on("locationerror", () => {
        problem =
          "This device didn't say where it is. Allow location, or search instead.";
      });

      map.on("pm:create", (event) => {
        shape = event.layer as Leaflet.Polygon;
        shape.setStyle(outlineStyle(place.kind, true));
        mode = "drawn";
      });

      // Start where it makes most sense: this place, then the place it's in, then everything drawn, then the
      // nursery's own postcode.
      const own = outlines[place.id];
      const around = place.parentId ? outlines[place.parentId] : undefined;

      if (own) {
        shape = L.polygon(own, outlineStyle(place.kind, true)).addTo(map);
        map.fitBounds(shape.getBounds(), { padding: [48, 48], maxZoom: 21 });
        mode = "drawn";
      } else if (around) {
        map.fitBounds(L.polygon(around).getBounds(), {
          padding: [24, 24],
          maxZoom: 20,
        });
      } else if (others.getLayers().length > 0) {
        map.fitBounds(others.getBounds(), { padding: [24, 24], maxZoom: 20 });
      } else {
        const details = (await api.GET("/api/settings/nursery")).data;
        const ours = postcodeIn(details?.address);

        if (ours && !search) {
          search = details?.address?.replace(/\s*\n\s*/g, ", ") ?? ours;
          await find();
        }
      }

      if (!own) {
        startDrawing();
      }

      ready = true;
    }, setProblem);

    return () => {
      stopped = true;
      map?.remove();
      map = null;
    };
  });
</script>

<div class="place-map">
  <div
    class="place-map-canvas"
    bind:this={box}
    role="application"
    aria-label="Map for drawing {place.name}"
  ></div>

  <aside class="place-map-panel stack">
    <div class="form-card stack">
      <h2 class="form-heading">Where is {place.name}?</h2>
      <p class="muted">{kindLabel(place.kind)}</p>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      <form class="row" onsubmit={find}>
        <label class="grow"
          >{key ? "Find the address or postcode" : "Find a postcode"}
          <input bind:value={search} autocomplete="street-address" /></label
        >
        <button type="submit" class="secondary">Go</button>
      </form>
      <button class="secondary" onclick={whereIAm}>Where I am</button>

      {#if mode === "drawing"}
        <p>
          Zoom in, then tap each corner of {place.name} in turn. Tap the first corner
          again, or press Finish, to close the shape.
        </p>
        <div class="row">
          <button disabled={!ready} onclick={finishDrawing}>Finish</button>
          <button class="secondary" disabled={!ready} onclick={startDrawing}
            >Start again</button
          >
        </div>
      {:else if mode === "editing"}
        <p>
          Drag a corner to move it, or the faint middle point of a side to add a
          corner.
        </p>
        <button disabled={busy} onclick={save}>Save the outline</button>
      {:else}
        <p>Is that the right shape?</p>
        <div class="row">
          <button disabled={busy} onclick={save}>Save the outline</button>
          <button class="secondary" onclick={startEditing}
            >Adjust corners</button
          >
        </div>
        <button class="link-button" onclick={startDrawing}>Draw it again</button
        >
      {/if}

      <button class="link-button" onclick={onSkip}>{skipLabel}</button>
    </div>

    {#if !satellite}
      <p class="muted">
        This is a street map. For satellite pictures, add an Esri key under
        Settings, Map.
      </p>
    {/if}
  </aside>
</div>
