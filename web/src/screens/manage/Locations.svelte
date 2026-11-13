<script lang="ts">
  import { isOn } from "../../lib/features.svelte";
  import ChevronRight from "@lucide/svelte/icons/chevron-right";
  import FormPanel from "../FormPanel.svelte";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { nestLocations } from "../../lib/locations";
  import { kindLabel, type Place } from "../../lib/places";
  import { attempt } from "../../lib/types";
  import PlaceView from "../work/PlaceView.svelte";
  import AddPlaces from "./AddPlaces.svelte";
  import ArchivedEntries from "./ArchivedEntries.svelte";
  import PlaceMap from "./PlaceMap.svelte";

  let {
    onBack,
    onOpenTray,
    onMoveTraysHere,
  }: {
    onBack: () => void;
    onOpenTray: (label: string) => void;
    onMoveTraysHere: (id: string) => void;
  } = $props();

  let places = $state<Place[]>([]);
  let openId = $state<string | null>(null);
  // The map is remembered on this device, so someone who prefers it keeps it.
  let showing = $state<"list" | "map">(rememberedView());

  function rememberedView(): "list" | "map" {
    try {
      return localStorage.getItem("growlog.placesView") === "map"
        ? "map"
        : "list";
    } catch {
      return "list";
    }
  }

  function show(view: "list" | "map") {
    showing = view;

    try {
      localStorage.setItem("growlog.placesView", view);
    } catch {
      // Without storage it simply opens on the list next time.
    }
  }
  let problem = $state("");

  const nested = $derived(nestLocations(places));
  const setProblem = (message: string) => (problem = message);

  async function load() {
    places = (await api.GET("/api/locations")).data ?? [];
  }

  onMount(() => void attempt(load, setProblem));
</script>

{#if openId}
  {#key openId}
    <PlaceView
      placeId={openId}
      canManage
      onBack={() => {
        openId = null;
        void attempt(load, setProblem);
      }}
      {onOpenTray}
      onOpenPlace={(id) => (openId = id)}
      {onMoveTraysHere}
    />
  {/key}
{:else}
  <main class:wide-map={showing === "map" && isOn("PlacesMap")}>
    <div class="title-row">
      <h1>Places</h1>
      {#if isOn("PlacesMap")}
        <div class="segmented two" role="group" aria-label="Show places as">
          <button
            class:secondary={showing !== "list"}
            aria-pressed={showing === "list"}
            onclick={() => show("list")}>List</button
          >
          <button
            class:secondary={showing !== "map"}
            aria-pressed={showing === "map"}
            onclick={() => show("map")}>Map</button
          >
        </div>
      {/if}
    </div>

    {#if showing === "map" && isOn("PlacesMap")}
      <PlaceMap {places} onOpen={(id) => (openId = id)} />
    {:else}
      <div class="split">
        <section>
          <FormPanel label="Add a place">
            <AddPlaces {places} onAdded={load} />
          </FormPanel>
        </section>
        <section>
          {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

          <ul class="place-tree">
            {#each nested as { place, depth } (place.id)}
              <li style:--depth={depth}>
                <button class="place-row" onclick={() => (openId = place.id)}>
                  <span class="plant-names">
                    <strong>{place.name}</strong>
                    <em>{kindLabel(place.kind)} · {place.labelCode}</em>
                  </span>
                  {#if place.photoIds.length > 0}
                    <span class="muted credit"
                      >{place.photoIds.length} photo{place.photoIds.length === 1
                        ? ""
                        : "s"}</span
                    >
                  {/if}
                  <ChevronRight size={18} />
                </button>
              </li>
            {:else}
              <li class="muted">
                No places yet. Start with the site, such as the farm.
              </li>
            {/each}
          </ul>
          <ArchivedEntries list="locations" onRestored={load} />
        </section>
      </div>
    {/if}

    <button class="back mobile-only" onclick={onBack}>Back</button>
  </main>
{/if}
