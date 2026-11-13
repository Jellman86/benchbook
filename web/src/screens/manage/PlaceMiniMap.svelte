<script lang="ts">
  import type * as Leaflet from "leaflet";
  import { onMount, tick } from "svelte";
  import { api } from "../../api/client";
  import { alwaysNamed, outlineStyle, type Point } from "../../lib/placeMap";
  import { loadLeaflet, openPlaceMap } from "../../lib/placeMapView";
  import { withEverythingInside, type Place } from "../../lib/places";

  // A place on its own page: its outline, with the places inside it faintly and named, and a way to draw or change it.
  let {
    place,
    places,
    onDraw,
  }: {
    place: Place;
    places: Place[];
    onDraw?: () => void;
  } = $props();

  let box = $state<HTMLDivElement | null>(null);
  let drawn = $state<boolean | null>(null);
  let map: Leaflet.Map | null = null;

  onMount(() => {
    let stopped = false;

    void (async () => {
      try {
        const { data } = await api.GET("/api/locations/map");
        const outlines: Record<string, Point[]> = Object.fromEntries(
          (data?.outlines ?? []).map((outline) => [
            outline.id,
            outline.points as Point[],
          ]),
        );
        const own = outlines[place.id];
        drawn = !!own;

        if (!own || stopped) {
          return;
        }

        // The box only exists once it's known there's something to show.
        await tick();
        const L = await loadLeaflet(false);

        if (stopped || !box) {
          return;
        }

        const view = await openPlaceMap(L, box, data ?? {}, { quiet: true });
        map = view.map;
        const inside = withEverythingInside(place.id, places);

        for (const other of places) {
          const points = outlines[other.id];

          if (!points || other.id === place.id || !inside.has(other.id)) {
            continue;
          }

          const layer = L.polygon(points.map(view.toMap), {
            ...outlineStyle(other.kind),
            interactive: false,
          });
          // Names go in as text, never as markup.
          const label = document.createElement("span");
          label.textContent = other.name;
          layer.bindTooltip(label, {
            permanent: alwaysNamed(other.kind),
            direction: "center",
            className: "place-map-label",
          });
          layer.addTo(map);
        }

        const shape = L.polygon(
          own.map(view.toMap),
          outlineStyle(place.kind, true),
        ).addTo(map);
        map.fitBounds(shape.getBounds(), { padding: [24, 24], maxZoom: 20 });
      } catch {
        // Without the map, the rest of the page still works.
        drawn = null;
      }
    })();

    return () => {
      stopped = true;
      map?.remove();
      map = null;
    };
  });
</script>

{#if drawn === true}
  <figure class="place-mini-map card">
    <div
      class="place-mini-map-canvas"
      bind:this={box}
      role="img"
      aria-label="{place.name} on the map"
    ></div>
    {#if onDraw}
      <button class="secondary" onclick={onDraw}>Change it on the map</button>
    {/if}
  </figure>
{:else if drawn === false && onDraw}
  <div class="card stack tight">
    <p class="muted">{place.name} isn't on the map yet.</p>
    <button class="secondary" onclick={onDraw}>Draw it on the map</button>
  </div>
{/if}
