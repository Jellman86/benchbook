<script lang="ts">
  import { lineAmount } from "../lib/sowing";
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
    chipTotal,
    uncountedTrays,
    type ProductionRow,
    type Stage,
  } from "../lib/production";
  import { plantName } from "../lib/work";
  import type { Column } from "../lib/table";
  import DataTable from "./DataTable.svelte";
  import PlantThumb from "./work/PlantThumb.svelte";
  import ManyTrays from "./work/ManyTrays.svelte";

  let {
    onOpen,
    onBack,
    onLabels,
  }: {
    onOpen: (label: string) => void;
    onBack: () => void;
    onLabels: (labelCodes: string[]) => void;
  } = $props();

  // Choosing several trays to work on at once, instead of opening one.
  let choosing = $state(false);
  let chosen = $state<Record<string, string>>({});
  let notice = $state("");

  const chosenTrays = $derived(
    Object.entries(chosen).map(([id, labelCode]) => ({ id, labelCode })),
  );

  function tap(row: ProductionRow) {
    if (!choosing) {
      onOpen(row.labelCode);
      return;
    }

    const next = { ...chosen };

    if (next[row.containerId]) {
      delete next[row.containerId];
    } else {
      next[row.containerId] = row.labelCode;
    }

    chosen = next;
  }

  function chooseShown() {
    chosen = Object.fromEntries([
      ...Object.entries(chosen),
      ...shown.map((row) => [row.containerId, row.labelCode]),
    ]);
  }

  async function finished(message: string) {
    notice = message;
    chosen = {};
    choosing = false;
    await load();
  }

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

  const columns: Column<ProductionRow>[] = $derived([
    { label: "Label", value: (row) => row.labelCode },
    { label: "Plant", value: (row) => plantName(row.commonName, row.variety) },
    {
      label: "Plants",
      value: (row) => Number(row.quantity),
      text: (row) =>
        row.notCounted ? "Not counted yet" : String(row.quantity),
      numeric: true,
    },
    { label: "Stage", value: (row) => stageLabel(row.stage) },
    {
      label: "Days at stage",
      value: (row) => daysAt(row.stageSince),
      numeric: true,
    },
    { label: "Place", value: (row) => row.locationName },
    { label: "Pot", value: (row) => row.potSizeName },
    { label: "Supplier", value: (row) => row.supplierName },
  ]);

  // The stage tabs count what the other filters leave, so the numbers match the list.
  const matching = $derived(filterRows(rows, { text, place, supplier }));
  const counts = $derived(plantsByStage(matching));
  const uncounted = $derived(uncountedTrays(matching));
  const shown = $derived(
    oldestFirst(
      stage ? matching.filter((row) => row.stage === stage) : matching,
    ),
  );
  const visibleStages = $derived(
    stages.filter(
      (item) =>
        item.always ||
        counts.has(item.stage) ||
        uncounted.byStage.has(item.stage),
    ),
  );
  const places = $derived(choicesOf(rows.map((row) => row.locationName)));
  const suppliers = $derived(choicesOf(rows.map((row) => row.supplierName)));

  async function load() {
    try {
      rows = (await api.GET("/api/production")).data ?? [];
    } catch {
      problem =
        "This device can't reach Hortiplanner. Please check the connection and try again.";
    } finally {
      loaded = true;
    }
  }

  onMount(load);
</script>

<main>
  <h1>Production</h1>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if notice}<p class="card done" role="status">{notice}</p>{/if}

  <div class="row">
    <button
      class="secondary"
      aria-pressed={choosing}
      onclick={() => {
        choosing = !choosing;
        chosen = {};
        notice = "";
      }}>{choosing ? "Stop choosing" : "Choose several"}</button
    >
    {#if choosing}
      <button class="secondary" onclick={chooseShown}
        >Choose all {shown.length} shown</button
      >
    {/if}
  </div>
  {#if choosing}
    <p class="muted">
      Tap or click trays to choose them, then pick what to do with them all
      below.
    </p>
  {/if}

  <div class="chips" role="group" aria-label="Stage">
    <button aria-pressed={stage === null} onclick={() => (stage = null)}>
      All <span class="chip-count"
        >{chipTotal(totalPlants(matching), uncounted.all)}</span
      >
    </button>
    {#each visibleStages as item (item.stage)}
      <button
        aria-pressed={stage === item.stage}
        onclick={() => (stage = item.stage)}
      >
        {item.label}
        <span class="chip-count"
          >{chipTotal(
            counts.get(item.stage) ?? 0,
            uncounted.byStage.get(item.stage) ?? 0,
          )}</span
        >
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
        ? "Nothing is growing yet. Start a new batch to see it here."
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
        onSelect={tap}
        chosen={choosing ? (row) => !!chosen[row.containerId] : undefined}
      />
    </div>
  {/if}

  <div class="mobile-only">
    <div class="card-grid">
      {#each shown as row (`${row.containerId}/${row.batchId}`)}
        <button
          class="plant-card production-row"
          class:chosen={!!chosen[row.containerId]}
          aria-pressed={choosing ? !!chosen[row.containerId] : undefined}
          onclick={() => tap(row)}
        >
          <PlantThumb
            url={row.hasPhoto ? `/api/plants/${row.plantId}/photo` : null}
          />
          <span class="plant-names">
            <strong
              >{lineAmount(row)}
              {plantName(row.commonName, row.variety)}</strong
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

  {#if choosing && chosenTrays.length > 0}
    <ManyTrays
      trays={chosenTrays}
      onDone={finished}
      {onLabels}
      onClear={() => (chosen = {})}
      onKeepOnly={(ids) => {
        chosen = Object.fromEntries(
          Object.entries(chosen).filter(([id]) => ids.includes(id)),
        );
        void load();
      }}
    />
  {/if}

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
