<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import { plantName, type PickedPlant } from "../../lib/work";
  import PlantCard from "../work/PlantCard.svelte";
  import PlantSearch from "../work/PlantSearch.svelte";
  import PlantThumb from "../work/PlantThumb.svelte";

  let {
    onBack,
    canManage = true,
  }: {
    onBack: () => void;
    canManage?: boolean;
  } = $props();

  type Plant = Schemas["PlantResponse"];
  type SpeciesPick = Extract<PickedPlant, { kind: "species" }>;

  const empty = {
    commonName: "",
    variety: "",
    botanicalName: "",
    category: "",
    position: "",
    watering: "",
    hardiness: "",
    xylellaHost: false,
    protectedVariety: false,
  };

  let plants = $state<Plant[]>([]);
  let potSizes = $state<Schemas["PotSizeResponse"][]>([]);
  let form = $state({ ...empty });
  let prices = $state<Record<string, string>>({});
  let editing = $state<string | null>(null);
  let species = $state<SpeciesPick | null>(null);
  let details = $state<Schemas["SpeciesDetailsResponse"] | null>(null);
  let showForm = $state(false);
  let cardId = $state<string | null>(null);
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  const selected = $derived(plants.find((plant) => plant.id === cardId));
  const suggested = $derived(
    !!details?.suggestedPosition ||
      !!details?.suggestedWatering ||
      !!details?.suggestedHardiness,
  );

  async function load() {
    plants = (await api.GET("/api/plants")).data ?? [];
    potSizes = (await api.GET("/api/pot-sizes")).data ?? [];
  }

  onMount(() => void attempt(load, setProblem));

  function reset() {
    form = { ...empty };
    prices = {};
    editing = null;
    species = null;
    details = null;
    showForm = false;
    problem = "";
  }

  const pick = (choice: PickedPlant) => {
    problem = "";

    if (choice.kind === "plant") {
      cardId = choice.id;
      return;
    }

    form = { ...empty };
    prices = {};
    editing = null;
    details = null;

    if (choice.kind === "new") {
      species = null;
      form.commonName = choice.commonName;
      form.variety = choice.variety ?? "";
      form.botanicalName = choice.botanicalName ?? "";
      showForm = true;
      return;
    }

    species = choice;
    form.commonName = choice.commonName ?? choice.scientificName;
    form.botanicalName = choice.scientificName;
    showForm = true;

    return attempt(async () => {
      const { data } = await api.GET("/api/species/{id}/details", {
        params: { path: { id: choice.id } },
      });

      details = data ?? null;
      form.position = form.position || (data?.suggestedPosition ?? "");
      form.watering = form.watering || (data?.suggestedWatering ?? "");
      form.hardiness = form.hardiness || (data?.suggestedHardiness ?? "");
    }, setProblem);
  };

  function edit(plant: Plant) {
    editing = plant.id;
    species = null;
    details = null;
    form = {
      commonName: plant.commonName,
      variety: plant.variety ?? "",
      botanicalName: plant.botanicalName ?? "",
      category: plant.category ?? "",
      position: plant.position ?? "",
      watering: plant.watering ?? "",
      hardiness: plant.hardiness ?? "",
      xylellaHost: plant.xylellaHost,
      protectedVariety: plant.protectedVariety,
    };
    prices = Object.fromEntries(
      plant.prices.map((price) => [price.potSizeId, String(price.price)]),
    );
    cardId = null;
    showForm = true;
  }

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    const shared = {
      variety: optional(form.variety),
      category: optional(form.category),
      position: optional(form.position),
      watering: optional(form.watering),
      hardiness: optional(form.hardiness),
      xylellaHost: form.xylellaHost,
      protectedVariety: form.protectedVariety,
      prices: Object.entries(prices)
        .filter(([, price]) => price.trim() !== "")
        .map(([potSizeId, price]) => ({ potSizeId, price: Number(price) })),
    };
    const body = {
      ...shared,
      commonName: form.commonName,
      botanicalName: optional(form.botanicalName),
    };

    return attempt(async () => {
      const { response } = species
        ? await api.POST("/api/plants/from-species", {
            body: { ...shared, speciesId: species.id },
          })
        : editing
          ? await api.PUT("/api/plants/{id}", {
              params: { path: { id: editing } },
              body,
            })
          : await api.POST("/api/plants", { body });

      if (response.ok) {
        reset();
        await load();
      } else {
        problem =
          response.status === 409
            ? "That plant is already on the list."
            : "Please check the name and prices.";
      }
    }, setProblem);
  };

  const archive = (id: string) =>
    attempt(async () => {
      await api.POST("/api/plants/{id}/archive", { params: { path: { id } } });
      cardId = null;
      await load();
    }, setProblem);
</script>

{#if cardId}
  <PlantCard
    plantId={cardId}
    {canManage}
    onBack={() => {
      cardId = null;
      void attempt(load, setProblem);
    }}
  >
    {#snippet actions()}
      {#if canManage && selected}
        <div class="row">
          <button class="secondary" onclick={() => edit(selected)}
            >Change</button
          >
          <a
            class="button secondary"
            href="/api/labels/bench-cards/{selected.id}"
            target="_blank"
            rel="noopener">Bench card</a
          >
          {#each selected.prices as price (price.potSizeId)}
            <a
              class="button secondary"
              href="/api/labels/plants/{selected.id}?potSizeId={price.potSizeId}"
              target="_blank"
              rel="noopener"
              >Plant label: {potSizes.find(
                (size) => size.id === price.potSizeId,
              )?.name}</a
            >
          {:else}
            <a
              class="button secondary"
              href="/api/labels/plants/{selected.id}"
              target="_blank"
              rel="noopener">Plant label</a
            >
          {/each}
          <button class="secondary" onclick={() => archive(selected.id)}
            >No longer grown</button
          >
        </div>
      {/if}
    {/snippet}
  </PlantCard>
{:else if showForm}
  <main>
    <h1>{editing ? "Change plant" : "Add a plant"}</h1>

    {#if species}
      <div class="card">
        <div class="plant-head">
          <PlantThumb url={species.photoUrl} />
          <span class="plant-names">
            <strong>{species.commonName ?? species.scientificName}</strong>
            <em>{species.scientificName}</em>
          </span>
        </div>
        {#if details?.family}<p class="muted">Family: {details.family}</p>{/if}
        {#if details?.description}<p class="article-text">
            {details.description.text}
          </p>{/if}
        {#if species.credit}<p class="muted credit">{species.credit}</p>{/if}
      </div>
      {#if suggested}
        <p class="muted">
          Position, watering and hardiness are filled in from Wikipedia. Please
          check them before adding.
        </p>
      {/if}
    {/if}

    <form class="stack narrow" onsubmit={save}>
      {#if !species}
        <label
          >Common name <input bind:value={form.commonName} required /></label
        >
      {/if}
      <label>Variety <input bind:value={form.variety} /></label>
      {#if !species}
        <label>Botanical name <input bind:value={form.botanicalName} /></label>
      {/if}
      <label>Category <input bind:value={form.category} /></label>
      <label>Position <input bind:value={form.position} /></label>
      <label>Watering <input bind:value={form.watering} /></label>
      <label>Hardiness <input bind:value={form.hardiness} /></label>
      <label class="tick">
        <input type="checkbox" bind:checked={form.xylellaHost} /> Xylella host
      </label>
      <label class="tick">
        <input type="checkbox" bind:checked={form.protectedVariety} /> Protected variety
      </label>
      {#if canManage}
        {#each potSizes as size (size.id)}
          <label
            >Price for {size.name} (£)
            <input bind:value={prices[size.id]} inputmode="decimal" />
          </label>
        {/each}
      {/if}
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
      <button type="submit">{editing ? "Save changes" : "Add"}</button>
      <button type="button" class="secondary" onclick={reset}>Cancel</button>
    </form>
  </main>
{:else}
  <main>
    <h1>Plants</h1>
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

    <PlantSearch
      recent={plants}
      onPick={pick}
      showRecent={false}
      label="Find or add a plant"
    />

    <div class="card-grid">
      {#each plants as plant (plant.id)}
        <button class="plant-card" onclick={() => (cardId = plant.id)}>
          <PlantThumb
            url={plant.hasPhoto ? `/api/plants/${plant.id}/photo` : null}
          />
          <span class="plant-names">
            <strong>{plantName(plant.commonName, plant.variety)}</strong>
            {#if plant.botanicalName}<em>{plant.botanicalName}</em>{/if}
          </span>
        </button>
      {:else}
        <p class="muted">No plants yet. Search above to add the first one.</p>
      {/each}
    </div>

    <button class="back mobile-only" onclick={onBack}>Back</button>
  </main>
{/if}
