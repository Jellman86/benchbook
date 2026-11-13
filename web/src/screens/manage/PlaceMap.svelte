<script lang="ts">
  import type * as Leaflet from "leaflet";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { nestLocations } from "../../lib/locations";
  import {
    alwaysNamed,
    corners,
    imagery,
    outlineStyle,
    postcodeIn,
    type Point,
  } from "../../lib/placeMap";
  import { kindLabel, type Place } from "../../lib/places";
  import { attempt } from "../../lib/types";

  // Places drawn over a satellite picture: tap one to open it, or pick a place and trace its corners.
  let { places, onOpen }: { places: Place[]; onOpen: (id: string) => void } =
    $props();

  type Mode = "looking" | "drawing" | "editing";

  let box = $state<HTMLDivElement | null>(null);
  let outlines = $state<Record<string, Point[]>>({});
  let satellite = $state(true);
  let chosenId = $state<string | null>(null);
  let mode = $state<Mode>("looking");
  let postcode = $state("");
  let problem = $state("");
  let ready = $state(false);

  let L: typeof Leaflet;
  let map: Leaflet.Map | null = null;
  let shapes: Leaflet.FeatureGroup | null = null;
  // Plain, not reactive: Leaflet owns these.
  let layers: Record<string, Leaflet.Polygon> = {};

  const nested = $derived(nestLocations(places));
  const chosen = $derived(places.find((place) => place.id === chosenId));
  const drawn = $derived(nested.filter(({ place }) => outlines[place.id]));
  const notDrawn = $derived(nested.filter(({ place }) => !outlines[place.id]));
  const setProblem = (message: string) => (problem = message);

  async function load(fit: boolean) {
    const { data } = await api.GET("/api/locations/map");
    outlines = Object.fromEntries(
      (data?.outlines ?? []).map((outline) => [
        outline.id,
        outline.points as Point[],
      ]),
    );
    draw();

    if (fit) {
      fitAll();
    }

    return data?.imageryKey ?? null;
  }

  // Parents first, so a bench sits on top of its greenhouse and can be tapped.
  function draw() {
    if (!map || !shapes) {
      return;
    }

    shapes.clearLayers();
    layers = {};

    for (const { place } of nested) {
      const points = outlines[place.id];

      if (!points) {
        continue;
      }

      const layer = L.polygon(
        points,
        outlineStyle(place.kind, place.id === chosenId),
      );
      const label = document.createElement("span");
      label.textContent = place.name;
      layer.bindTooltip(label, {
        permanent: alwaysNamed(place.kind) || place.id === chosenId,
        direction: "center",
        className: "place-map-label",
      });
      layer.on("click", () => choose(place.id));
      layer.addTo(shapes);
      layers[place.id] = layer;
    }
  }

  function fitAll() {
    if (map && shapes && shapes.getLayers().length > 0) {
      map.fitBounds(shapes.getBounds(), { padding: [24, 24], maxZoom: 20 });
    }
  }

  function choose(id: string | null) {
    if (mode !== "looking") {
      return;
    }

    chosenId = id;
    draw();
    const layer = id ? layers[id] : undefined;

    if (layer && map) {
      map.fitBounds(layer.getBounds(), { padding: [48, 48], maxZoom: 21 });
    }
  }

  async function saveOutline(id: string, points: Point[] | null) {
    const { response } = await api.PUT("/api/locations/{id}/outline", {
      params: { path: { id } },
      body: { points },
    });
    problem = response.ok
      ? ""
      : "That outline couldn't be saved. It needs at least three corners.";
    await load(false);
  }

  function startDrawing() {
    if (!map || !chosen) {
      return;
    }

    mode = "drawing";
    problem = "";
    map.pm.enableDraw("Polygon", {
      allowSelfIntersection: false,
      snappable: true,
      snapDistance: 16,
      tooltips: false,
      templineStyle: { color: "#ffffff" },
      hintlineStyle: { color: "#ffffff", dashArray: "5 5" },
      pathOptions: outlineStyle(chosen.kind, true),
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
    const layer = chosenId ? layers[chosenId] : undefined;

    if (!layer) {
      return;
    }

    mode = "editing";
    problem = "";
    layer.unbindTooltip();
    layer.pm.enable({ allowSelfIntersection: false, snappable: true });
  }

  const saveEditing = () =>
    attempt(async () => {
      const layer = chosenId ? layers[chosenId] : undefined;

      if (!layer || !chosenId) {
        return;
      }

      const rings = layer.getLatLngs() as Leaflet.LatLng[][];
      layer.pm.disable();
      mode = "looking";
      await saveOutline(chosenId, corners(rings[0] ?? []));
    }, setProblem);

  function stop() {
    map?.pm.disableDraw();
    const layer = chosenId ? layers[chosenId] : undefined;
    layer?.pm.disable();
    mode = "looking";
    draw();
  }

  const takeOff = () => {
    if (
      !chosen ||
      !confirm(`Take ${chosen.name} off the map? The place itself stays.`)
    ) {
      return;
    }

    return attempt(() => saveOutline(chosen.id, null), setProblem);
  };

  const findPostcode = (event?: SubmitEvent) => {
    event?.preventDefault();

    return attempt(async () => {
      const code = postcodeIn(postcode);

      if (!code) {
        problem = "That doesn't look like a postcode.";
        return;
      }

      // postcodes.io is free and open (Open Government Licence), and only told the postcode.
      const response = await fetch(
        `https://api.postcodes.io/postcodes/${encodeURIComponent(code)}`,
      );
      const found = response.ok
        ? (
            (await response.json()) as {
              result?: { latitude: number; longitude: number };
            }
          ).result
        : undefined;

      if (found && map) {
        problem = "";
        map.setView([found.latitude, found.longitude], 18);
      } else {
        problem = `Couldn't find ${code}. Zoom in by hand instead.`;
      }
    }, setProblem);
  };

  function whereIAm() {
    map?.locate({ setView: true, maxZoom: 19, enableHighAccuracy: true });
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
      shapes = L.featureGroup().addTo(map);

      const key = await load(true);
      const tiles = imagery(key);
      satellite = tiles.satellite;
      L.tileLayer(tiles.url, {
        attribution: tiles.attribution,
        maxNativeZoom: tiles.maxNativeZoom,
        maxZoom: 22,
      })
        .addTo(map)
        .bringToBack();

      let me: Leaflet.CircleMarker | null = null;
      map.on("locationfound", (event) => {
        me?.remove();
        me = L.circleMarker(event.latlng, {
          radius: 8,
          color: "#ffffff",
          weight: 3,
          fillColor: "#4a5cc0",
          fillOpacity: 1,
        }).addTo(map!);
      });
      map.on("locationerror", () => {
        problem =
          "This device didn't say where it is. Allow location, or find the postcode instead.";
      });

      map.on("pm:create", (event) => {
        const layer = event.layer as Leaflet.Polygon;
        const rings = layer.getLatLngs() as Leaflet.LatLng[][];
        layer.remove();
        mode = "looking";

        if (chosenId) {
          void attempt(
            () => saveOutline(chosenId!, corners(rings[0] ?? [])),
            setProblem,
          );
        }
      });

      if (shapes.getLayers().length === 0) {
        const { data } = await api.GET("/api/settings/nursery");
        const ours = postcodeIn(data?.address);

        // Only if nobody has started typing one meanwhile.
        if (ours && !postcode) {
          postcode = ours;
          await findPostcode();
        }
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
    aria-label="Map of the nursery's places"
  ></div>

  <aside class="place-map-panel stack">
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

    {#if mode === "drawing" && chosen}
      <div class="form-card stack">
        <h2 class="form-heading">Drawing {chosen.name}</h2>
        <p>
          Tap each corner in turn. Tap the first corner again, or press Finish,
          to close the shape.
        </p>
        <div class="row">
          <button onclick={finishDrawing}>Finish</button>
          <button class="secondary" onclick={stop}>Cancel</button>
        </div>
      </div>
    {:else if mode === "editing" && chosen}
      <div class="form-card stack">
        <h2 class="form-heading">Changing {chosen.name}</h2>
        <p>
          Drag a corner to move it, or the faint middle point of a side to add a
          corner. Tap a corner to remove it.
        </p>
        <div class="row">
          <button onclick={saveEditing}>Save outline</button>
          <button class="secondary" onclick={stop}>Cancel</button>
        </div>
      </div>
    {:else if chosen}
      <div class="form-card stack">
        <h2 class="form-heading">{chosen.name}</h2>
        <p class="muted">{kindLabel(chosen.kind)} · {chosen.labelCode}</p>
        <div class="row">
          <button onclick={() => onOpen(chosen.id)}>Open</button>
          {#if outlines[chosen.id]}
            <button class="secondary" onclick={startEditing}
              >Change outline</button
            >
          {:else}
            <button class="secondary" disabled={!ready} onclick={startDrawing}
              >Draw its outline</button
            >
          {/if}
        </div>
        {#if outlines[chosen.id]}
          <button class="link-button" onclick={takeOff}
            >Take it off the map</button
          >
        {/if}
        <button class="link-button" onclick={() => choose(null)}
          >Choose another place</button
        >
      </div>
    {:else}
      <form class="row" onsubmit={findPostcode}>
        <label class="grow"
          >Find a postcode <input
            bind:value={postcode}
            autocomplete="postal-code"
            autocapitalize="characters"
          /></label
        >
        <button type="submit" class="secondary">Go</button>
      </form>
      <div class="row">
        <button class="secondary" onclick={whereIAm}>Where I am</button>
        {#if drawn.length > 0}
          <button class="secondary" onclick={fitAll}>Show everything</button>
        {/if}
      </div>

      {#if notDrawn.length > 0}
        <h2 class="settings-heading">Not on the map yet</h2>
        <p class="muted">Pick one, then trace its corners on the picture.</p>
        <ul class="place-tree">
          {#each notDrawn as { place, depth } (place.id)}
            <li style:--depth={depth}>
              <button class="place-row" onclick={() => choose(place.id)}>
                <span class="plant-names">
                  <strong>{place.name}</strong>
                  <em>{kindLabel(place.kind)}</em>
                </span>
              </button>
            </li>
          {/each}
        </ul>
      {/if}

      {#if drawn.length > 0}
        <h2 class="settings-heading">On the map</h2>
        <ul class="place-tree">
          {#each drawn as { place, depth } (place.id)}
            <li style:--depth={depth}>
              <button class="place-row" onclick={() => choose(place.id)}>
                <span class="plant-names">
                  <strong>{place.name}</strong>
                  <em>{kindLabel(place.kind)}</em>
                </span>
              </button>
            </li>
          {/each}
        </ul>
      {/if}

      {#if !satellite}
        <p class="muted">
          This is a street map. For satellite pictures, add an Esri key under
          Settings, Map.
        </p>
      {/if}
    {/if}
  </aside>
</div>
