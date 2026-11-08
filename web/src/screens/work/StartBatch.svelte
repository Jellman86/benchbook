<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import {
    outcomeProblem,
    plantName,
    submit,
    work,
    workPlaces,
    type BatchPrefill,
  } from "../../lib/work";
  import ActionFrame from "./ActionFrame.svelte";
  import Stepper from "./Stepper.svelte";

  let {
    prefill,
    onDone,
    onCancel,
  }: {
    prefill?: BatchPrefill;
    onDone: (message: string, eventId: string, newLabel?: string) => void;
    onCancel: () => void;
  } = $props();

  let plants = $state<Schemas["PlantResponse"][]>([]);
  let suppliers = $state<Schemas["SupplierResponse"][]>([]);
  let places = $state<{ id: string; label: string }[]>([]);
  let potSizes = $state<Schemas["NamedEntry"][]>([]);

  let plantId = $state("");
  let quantity = $state(1);
  let source = $state<Schemas["BatchSource"]>("SeedLot");
  let sourceReference = $state("");
  let supplierId = $state("");
  let locationId = $state("");
  let potSizeId = $state("");
  let containerKind = $state<Schemas["ContainerKind"]>("Tray");
  let originCountry = $state("GB");
  let problem = $state("");
  let busy = $state(false);

  const setProblem = (message: string) => (problem = message);

  onMount(() =>
    attempt(async () => {
      plants = (await api.GET("/api/plants")).data ?? [];
      suppliers = (await api.GET("/api/suppliers")).data ?? [];
      places = workPlaces((await api.GET("/api/locations")).data ?? []);
      potSizes = (await api.GET("/api/pot-sizes")).data ?? [];
      plantId = plants[0]?.id ?? "";
      locationId = places[0]?.id ?? "";
      potSizeId = potSizes[0]?.id ?? "";

      if (prefill) {
        source = "BoughtIn";
        supplierId = prefill.supplierId;
        quantity = prefill.quantity;
      }
    }, setProblem),
  );

  async function start() {
    busy = true;
    const plant = plants.find((item) => item.id === plantId);
    const outcome = await submit(
      work.startBatch({
        plantId,
        quantity,
        source,
        sourceReference: optional(sourceReference),
        supplierId: supplierId || null,
        parentBatchId: null,
        motherPlant: null,
        originCountry,
        containerKind,
        locationId: locationId || null,
        potSizeId: potSizeId || null,
        purchaseLineId: prefill?.purchaseLineId ?? null,
      }),
    );
    busy = false;

    if (outcome.kind === "stored") {
      onDone(
        `Started ${quantity} ${plant ? plantName(plant.commonName, plant.variety) : ""}. Label: ${outcome.labelCode}`,
        outcome.eventId,
        outcome.labelCode,
      );
    } else {
      problem = outcomeProblem(outcome);
    }
  }
</script>

<ActionFrame title="New batch" {problem} {onCancel}>
  {#if prefill}<p class="muted">From purchase: {prefill.description}</p>{/if}
  {#if plants.length === 0}
    <p class="muted">No plants on the list yet. Ask the owner to add some.</p>
  {:else}
    <label>
      Plant
      <select bind:value={plantId}>
        {#each plants as plant (plant.id)}
          <option value={plant.id}
            >{plantName(plant.commonName, plant.variety)}</option
          >
        {/each}
      </select>
    </label>
    <p>How many?</p>
    <Stepper bind:value={quantity} presets={[10, 24, 40, 84]} />
    <label>
      Where from
      <select bind:value={source}>
        <option value="SeedLot">Seed</option>
        <option value="BoughtIn">Bought-in plants</option>
        <option value="Cuttings">Cuttings</option>
      </select>
    </label>
    {#if source !== "Cuttings"}
      <label>
        Supplier
        <select bind:value={supplierId}>
          <option value="">Not listed</option>
          {#each suppliers as supplier (supplier.id)}
            <option value={supplier.id}>{supplier.name}</option>
          {/each}
        </select>
      </label>
      <label>Lot or reference <input bind:value={sourceReference} /></label>
    {/if}
    <label>
      Country of origin
      <select bind:value={originCountry}>
        <option value="GB">Great Britain</option>
        <option value="IE">Ireland</option>
        <option value="NL">Netherlands</option>
        <option value="DE">Germany</option>
        <option value="FR">France</option>
        <option value="BE">Belgium</option>
        <option value="IT">Italy</option>
        <option value="ES">Spain</option>
      </select>
    </label>
    <label>
      Place
      <select bind:value={locationId}>
        <option value="">Not set</option>
        {#each places as place (place.id)}
          <option value={place.id}>{place.label}</option>
        {/each}
      </select>
    </label>
    <label>
      Pot size
      <select bind:value={potSizeId}>
        <option value="">Not set</option>
        {#each potSizes as size (size.id)}
          <option value={size.id}>{size.name}</option>
        {/each}
      </select>
    </label>
    <label>
      Going into
      <select bind:value={containerKind}>
        <option value="Tray">Tray</option>
        <option value="PotGroup">Group of pots</option>
        <option value="BenchLot">Bench lot</option>
      </select>
    </label>
    <button disabled={busy || !plantId} onclick={start}>Start batch</button>
  {/if}
</ActionFrame>
