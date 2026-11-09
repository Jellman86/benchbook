<script lang="ts">
  import { api } from "../../api/client";
  import type { Schemas } from "../../lib/types";
  import { plantName, type PickedPlant } from "../../lib/work";
  import PlantThumb from "./PlantThumb.svelte";

  type Own = Pick<
    Schemas["OwnPlantResult"],
    "id" | "commonName" | "variety" | "botanicalName" | "hasPhoto"
  >;

  let {
    recent,
    onPick,
  }: {
    recent: Own[];
    onPick: (picked: PickedPlant) => void;
  } = $props();

  const shortestSearch = 3;

  let query = $state("");
  let own = $state<Own[]>([]);
  let species = $state<Schemas["SpeciesResult"][]>([]);
  let searching = $state(false);
  let unavailable = $state(false);
  let byHand = $state(false);
  let commonName = $state("");
  let variety = $state("");
  let botanicalName = $state("");

  let timer: ReturnType<typeof setTimeout> | undefined;
  let latest = 0;
  let inFlight: AbortController | undefined;

  const typed = $derived(query.trim().length >= shortestSearch);
  const shown = $derived(typed ? own : recent.slice(0, 6));

  function matches(plant: Own, text: string) {
    return [plant.commonName, plant.variety, plant.botanicalName].some((name) =>
      name?.toLowerCase().includes(text),
    );
  }

  async function search(text: string, id: number, signal: AbortSignal) {
    try {
      const { data } = await api.GET("/api/species/search", {
        params: { query: { q: text } },
        signal,
      });

      if (id === latest && data) {
        own = data.plants as Own[];
        species = data.species as Schemas["SpeciesResult"][];
        unavailable = data.onlineUnavailable;
      }
    } catch {
      // No connection: the plants already on this screen can still be chosen.
      if (id === latest) {
        own = recent.filter((plant) => matches(plant, text.toLowerCase()));
        species = [];
        unavailable = true;
      }
    } finally {
      if (id === latest) {
        searching = false;
      }
    }
  }

  function typing() {
    clearTimeout(timer);
    inFlight?.abort();
    latest += 1;
    const id = latest;

    // Never leave the last search's plants showing, where one could be tapped by mistake.
    own = [];
    species = [];
    unavailable = false;

    if (!typed) {
      searching = false;
      return;
    }

    searching = true;
    const text = query.trim();
    inFlight = new AbortController();
    const { signal } = inFlight;
    timer = setTimeout(() => void search(text, id, signal), 400);
  }

  function pickOwn(plant: Own) {
    onPick({
      kind: "plant",
      id: plant.id,
      commonName: plant.commonName,
      variety: plant.variety ?? null,
      botanicalName: plant.botanicalName ?? null,
      photoUrl: plant.hasPhoto ? `/api/plants/${plant.id}/photo` : null,
    });
  }

  function pickSpecies(item: Schemas["SpeciesResult"]) {
    onPick({
      kind: "species",
      id: item.id,
      commonName: item.commonName ?? null,
      scientificName: item.scientificName,
      photoUrl: item.hasPhoto ? `/api/species/${item.id}/photo` : null,
      credit: item.photoCredit ?? null,
    });
  }

  function startByHand() {
    commonName = query.trim();
    byHand = true;
  }

  function finishByHand(event: SubmitEvent) {
    event.preventDefault();
    onPick({
      kind: "new",
      commonName: commonName.trim(),
      variety: variety.trim() || null,
      botanicalName: botanicalName.trim() || null,
    });
  }
</script>

{#if byHand}
  <form class="stack" onsubmit={finishByHand}>
    <label>Common name <input bind:value={commonName} required /></label>
    <label>Variety (if there is one) <input bind:value={variety} /></label>
    <label
      >Botanical name (if you know it) <input
        bind:value={botanicalName}
      /></label
    >
    <button type="submit">Next</button>
    <button type="button" class="secondary" onclick={() => (byHand = false)}
      >Search instead</button
    >
  </form>
{:else}
  <label>
    What are you planting?
    <input
      type="search"
      bind:value={query}
      oninput={typing}
      autocomplete="off"
      placeholder="Lavender, foxglove…"
    />
  </label>

  {#if searching}<p class="muted" role="status">Searching…</p>{/if}
  {#if unavailable}
    <p class="muted">
      Can't reach the online plant lists just now, so only saved plants are
      shown.
    </p>
  {/if}

  {#if shown.length > 0}
    <h2>{typed ? "Your plants" : "Recent plants"}</h2>
    <div class="card-grid">
      {#each shown as plant (plant.id)}
        <button class="plant-card" onclick={() => pickOwn(plant)}>
          <PlantThumb
            url={plant.hasPhoto ? `/api/plants/${plant.id}/photo` : null}
          />
          <span class="plant-names">
            <strong>{plantName(plant.commonName, plant.variety)}</strong>
            {#if plant.botanicalName}<em>{plant.botanicalName}</em>{/if}
          </span>
        </button>
      {/each}
    </div>
  {/if}

  {#if species.length > 0}
    <h2>Other plants</h2>
    <div class="card-grid">
      {#each species as item (item.id)}
        <button class="plant-card" onclick={() => pickSpecies(item)}>
          <PlantThumb
            url={item.hasPhoto ? `/api/species/${item.id}/photo` : null}
          />
          <span class="plant-names">
            <strong>{item.commonName ?? item.scientificName}</strong>
            {#if item.commonName}<em>{item.scientificName}</em>{/if}
          </span>
        </button>
      {/each}
    </div>
  {/if}

  {#if typed && !searching}
    {#if shown.length === 0 && species.length === 0}
      <p class="muted">Nothing found for “{query.trim()}”.</p>
    {/if}
    <button class="secondary" onclick={startByHand}
      >Not listed? Add it by hand</button
    >
  {/if}
{/if}
