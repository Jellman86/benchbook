<script lang="ts">
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
  <main>
    <h1>Places</h1>

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

    <button class="back mobile-only" onclick={onBack}>Back</button>
  </main>
{/if}
