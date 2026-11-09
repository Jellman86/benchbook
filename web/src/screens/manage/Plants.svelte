<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import { plantName, type PickedPlant } from "../../lib/work";
  import ArticleSection from "../work/ArticleSection.svelte";
  import Loading from "../work/Loading.svelte";
  import PlantCard from "../work/PlantCard.svelte";
  import PlantHero from "../work/PlantHero.svelte";
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
  let gathering = $state(false);
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

  const isSuggestion = (value: string, suggestion?: string | null) =>
    !!suggestion && value === suggestion;

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
    gathering = false;
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

    gathering = true;

    return attempt(async () => {
      try {
        const { data } = await api.GET("/api/species/{id}/details", {
          params: { path: { id: choice.id } },
        });

        // They may have cancelled and moved on while this was being fetched.
        if (species?.id === choice.id) {
          details = data ?? null;
          form.position = form.position || (data?.suggestedPosition ?? "");
          form.watering = form.watering || (data?.suggestedWatering ?? "");
          form.hardiness = form.hardiness || (data?.suggestedHardiness ?? "");
        }
      } finally {
        if (species?.id === choice.id) {
          gathering = false;
        }
      }
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

    <div class="plant-layout" class:single={!species}>
      {#if species}
        <aside class="plant-side">
          <PlantHero url={species.photoUrl} credit={species.credit} />
          <div class="plant-title">
            <h2>{species.commonName ?? species.scientificName}</h2>
            <p class="plant-botanical"><em>{species.scientificName}</em></p>
          </div>

          {#if details?.family || details?.nativeToUk != null}
            <ul class="tags appear">
              {#if details.family}<li>{details.family}</li>{/if}
              {#if details.nativeToUk === true}<li>Native to the UK</li>{/if}
              {#if details.nativeToUk === false}<li>
                  Introduced to the UK
                </li>{/if}
            </ul>
          {/if}

          {#if gathering}
            <span class="skeleton-lines" aria-hidden="true">
              <span class="skeleton"></span>
              <span class="skeleton"></span>
              <span class="skeleton"></span>
              <span class="skeleton short"></span>
            </span>
          {/if}
          {#if details?.description}
            <ArticleSection
              title="About this plant"
              article={details.description}
            />
          {/if}
          {#if details?.growing}
            <ArticleSection title="Growing it" article={details.growing} />
          {/if}
        </aside>
      {/if}

      <form class="plant-main" onsubmit={save}>
        <fieldset class="form-card">
          <legend>The plant</legend>
          <div class="field-grid">
            {#if !species}
              <label
                >Common name <input
                  bind:value={form.commonName}
                  required
                /></label
              >
            {/if}
            <label>Variety <input bind:value={form.variety} /></label>
            {#if !species}
              <label
                >Botanical name <input bind:value={form.botanicalName} /></label
              >
            {/if}
            <label>Category <input bind:value={form.category} /></label>
          </div>
        </fieldset>

        <fieldset class="form-card">
          <legend>Growing</legend>
          {#if species}
            <div class="form-hint">
              {#if gathering}
                <Loading text="Looking up how to grow it…" />
              {:else if details?.unavailable}
                <p class="muted">
                  Can't reach the online plant details just now. You can still
                  add it.
                </p>
              {:else if suggested}
                <p class="muted">
                  Marked boxes were filled in from Wikipedia. Please check them
                  before adding.
                </p>
              {:else}
                <p class="muted">
                  Nothing to suggest for this one, so add what you know.
                </p>
              {/if}
            </div>
          {/if}
          <div class="field-grid">
            <label
              >Position <input
                bind:value={form.position}
                class:suggested={isSuggestion(
                  form.position,
                  details?.suggestedPosition,
                )}
              /></label
            >
            <label
              >Watering <input
                bind:value={form.watering}
                class:suggested={isSuggestion(
                  form.watering,
                  details?.suggestedWatering,
                )}
              /></label
            >
            <label
              >Hardiness <input
                bind:value={form.hardiness}
                class:suggested={isSuggestion(
                  form.hardiness,
                  details?.suggestedHardiness,
                )}
              /></label
            >
          </div>
        </fieldset>

        <fieldset class="form-card">
          <legend>Rules that apply</legend>
          <div class="tick-row">
            <label class="tick">
              <input type="checkbox" bind:checked={form.xylellaHost} /> Xylella host
            </label>
            <label class="tick">
              <input type="checkbox" bind:checked={form.protectedVariety} /> Protected
              variety
            </label>
          </div>
        </fieldset>

        {#if canManage && potSizes.length > 0}
          <fieldset class="form-card">
            <legend>Prices</legend>
            <div class="field-grid">
              {#each potSizes as size (size.id)}
                <label
                  >Price for {size.name} (£)
                  <input bind:value={prices[size.id]} inputmode="decimal" />
                </label>
              {/each}
            </div>
          </fieldset>
        {/if}

        {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
        <div class="form-actions">
          <button type="submit">{editing ? "Save changes" : "Add"}</button>
          <button type="button" class="secondary" onclick={reset}>Cancel</button
          >
        </div>
      </form>
    </div>
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

    {#if plants.length > 0}
      <h2>All plants</h2>
    {:else}
      <p class="note-empty muted">
        No plants yet. Search above to add the first one.
      </p>
    {/if}
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
      {/each}
    </div>

    <button class="back mobile-only" onclick={onBack}>Back</button>
  </main>
{/if}
