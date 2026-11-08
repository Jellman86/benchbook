<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, optional, type Schemas } from "../../lib/types";

  let { onBack }: { onBack: () => void } = $props();

  type Plant = Schemas["PlantResponse"];

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
  let potSizes = $state<Schemas["NamedEntry"][]>([]);
  let form = $state({ ...empty });
  let prices = $state<Record<string, string>>({});
  let editing = $state<string | null>(null);
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    plants = (await api.GET("/api/plants")).data ?? [];
    potSizes = (await api.GET("/api/pot-sizes")).data ?? [];
  }

  onMount(() => void attempt(load, setProblem));

  function reset() {
    form = { ...empty };
    prices = {};
    editing = null;
  }

  function edit(plant: Plant) {
    editing = plant.id;
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
  }

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    const body = {
      commonName: form.commonName,
      variety: optional(form.variety),
      botanicalName: optional(form.botanicalName),
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

    return attempt(async () => {
      const { response } = editing
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
      await load();
    }, setProblem);
</script>

<main>
  <h1>Plants</h1>

  <form class="stack" onsubmit={save}>
    <label>Common name <input bind:value={form.commonName} required /></label>
    <label>Variety <input bind:value={form.variety} /></label>
    <label>Botanical name <input bind:value={form.botanicalName} /></label>
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
    {#each potSizes as size (size.id)}
      <label
        >Price for {size.name} (£)
        <input bind:value={prices[size.id]} inputmode="decimal" />
      </label>
    {/each}
    <button type="submit">{editing ? "Save changes" : "Add"}</button>
    {#if editing}
      <button type="button" class="secondary" onclick={reset}>Cancel</button>
    {/if}
  </form>

  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  {#each plants as plant (plant.id)}
    <div class="card">
      <strong
        >{plant.commonName}{plant.variety ? ` '${plant.variety}'` : ""}</strong
      >
      {#if plant.botanicalName}<em>{plant.botanicalName}</em>{/if}
      <div class="row">
        <button class="secondary" onclick={() => edit(plant)}>Change</button>
        <button class="secondary" onclick={() => archive(plant.id)}
          >No longer grown</button
        >
      </div>
    </div>
  {/each}

  <button class="secondary" onclick={onBack}>Back</button>
</main>
