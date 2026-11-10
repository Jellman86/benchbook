<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../api/client";
  import {
    choicesOf,
    filterRows,
    oldestFirst,
    plantsByStage,
    stageLabel,
    stages,
    timeAtStage,
    totalPlants,
    type ProductionRow,
    type Stage,
  } from "../lib/production";
  import { plantName } from "../lib/work";
  import type { Column } from "../lib/table";
  import DataTable from "./DataTable.svelte";
  import PlantThumb from "./work/PlantThumb.svelte";

  let {
    onOpen,
    onBack,
  }: {
    onOpen: (label: string) => void;
    onBack: () => void;
  } = $props();

  let rows = $state<ProductionRow[]>([]);
  let loaded = $state(false);
  let problem = $state("");
  let stage = $state<Stage | null>(null);
  let text = $state("");
  let place = $state("");
  let supplier = $state("");

  const now = new Date();
  const daysAt = (since: string | null | undefined) =>
    since
      ? Math.floor((now.getTime() - new Date(since).getTime()) / 86_400_000)
      : null;

  const columns: Column<ProductionRow>[] = [
    { label: "Label", value: (row) => row.labelCode },
    { label: "Plant", value: (row) => plantName(row.commonName, row.variety) },
    { label: "Plants", value: (row) => Number(row.quantity), numeric: true },
    { label: "Stage", value: (row) => stageLabel(row.stage) },
    {
      label: "Days at stage",
      value: (row) => daysAt(row.stageSince),
      numeric: true,
    },
    { label: "Place", value: (row) => row.locationName },
    { label: "Pot", value: (row) => row.potSizeName },
    { label: "Supplier", value: (row) => row.supplierName },
  ];

  // The stage tabs count what the other filters leave, so the numbers match the list.
  const matching = $derived(filterRows(rows, { text, place, supplier }));
  const counts = $derived(plantsByStage(matching));
  const shown = $derived(
    oldestFirst(
      stage ? matching.filter((row) => row.stage === stage) : matching,
    ),
  );
  const visibleStages = $derived(
    stages.filter((item) => item.always || counts.has(item.stage)),
  );
  const places = $derived(choicesOf(rows.map((row) => row.locationName)));
  const suppliers = $derived(choicesOf(rows.map((row) => row.supplierName)));

  onMount(async () => {
    try {
      rows = (await api.GET("/api/production")).data ?? [];
    } catch {
      problem = "Can't reach GrowLog. Check the connection and try again.";
    } finally {
      loaded = true;
    }
  });
</script>

<main>
  <h1>Production</h1>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  <div class="chips" role="group" aria-label="Stage">
    <button aria-pressed={stage === null} onclick={() => (stage = null)}>
      All <span class="chip-count">{totalPlants(matching)}</span>
    </button>
    {#each visibleStages as item (item.stage)}
      <button
        aria-pressed={stage === item.stage}
        onclick={() => (stage = item.stage)}
      >
        {item.label}
        <span class="chip-count">{counts.get(item.stage) ?? 0}</span>
      </button>
    {/each}
  </div>

  <div class="filters">
    <label>
      Find
      <input
        type="search"
        bind:value={text}
        autocomplete="off"
        placeholder="Plant, label or batch"
      />
    </label>
    <label>
      Place
      <select bind:value={place}>
        <option value="">All places</option>
        {#each places as name (name)}<option value={name}>{name}</option>{/each}
      </select>
    </label>
    <label>
      Supplier
      <select bind:value={supplier}>
        <option value="">All suppliers</option>
        {#each suppliers as name (name)}<option value={name}>{name}</option
          >{/each}
      </select>
    </label>
  </div>

  {#if loaded && shown.length === 0 && !problem}
    <p class="muted">
      {rows.length === 0
        ? "Nothing growing yet. Start a new batch to see it here."
        : "Nothing matches that."}
    </p>
  {/if}

  {#if shown.length > 0}
    <div class="desktop-only">
      <DataTable
        rows={shown}
        {columns}
        key={(row) => `${row.containerId}/${row.batchId}`}
        caption="Everything growing"
        onSelect={(row) => onOpen(row.labelCode)}
      />
    </div>
  {/if}

  <div class="mobile-only">
    <div class="card-grid">
      {#each shown as row (`${row.containerId}/${row.batchId}`)}
        <button
          class="plant-card production-row"
          onclick={() => onOpen(row.labelCode)}
        >
          <PlantThumb
            url={row.hasPhoto ? `/api/plants/${row.plantId}/photo` : null}
          />
          <span class="plant-names">
            <strong
              >{row.quantity} {plantName(row.commonName, row.variety)}</strong
            >
            <span class="muted">
              {[row.labelCode, row.locationName, row.potSizeName]
                .filter(Boolean)
                .join(" · ")}
            </span>
            <span class="muted">
              <span class="pill">{stageLabel(row.stage)}</span>
              {timeAtStage(row.stageSince, now)}
            </span>
          </span>
        </button>
      {/each}
    </div>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
