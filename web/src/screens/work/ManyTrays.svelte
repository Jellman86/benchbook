<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { manyTraysResult } from "../../lib/manyTrays";
  import { recordWork } from "../../lib/queue";
  import { work, workPlaces, type Outcome } from "../../lib/work";

  // One piece of work for several trays at once. Each tray is recorded on its own, so each waits on the device
  // without a signal, and one refused doesn't stop the rest.
  let {
    trays,
    onDone,
    onLabels,
    onClear,
    onKeepOnly,
  }: {
    trays: { id: string; labelCode: string }[];
    onDone: (message: string) => void;
    onLabels: (labelCodes: string[]) => void;
    onClear: () => void;
    /** After some couldn't be done: keep just these trays chosen. */
    onKeepOnly: (ids: string[]) => void;
  } = $props();

  type Doing = "ready" | "move" | "treat" | null;

  let doing = $state<Doing>(null);
  let places = $state<{ id: string; label: string }[]>([]);
  let placeId = $state("");
  let product = $state("");
  let dose = $state("");
  let area = $state("");
  let busy = $state(false);
  let problem = $state("");

  onMount(async () => {
    try {
      places = workPlaces((await api.GET("/api/locations")).data ?? []);
      placeId = places[0]?.id ?? "";
    } catch {
      places = [];
    }
  });

  async function forEachTray(
    make: (id: string) => Parameters<typeof recordWork>[0],
    done: (trays: string) => string,
  ) {
    busy = true;
    problem = "";
    const results: { id: string; labelCode: string; outcome: Outcome }[] = [];

    for (const tray of trays) {
      results.push({ ...tray, outcome: await recordWork(make(tray.id)) });
    }

    busy = false;
    const result = manyTraysResult(done, results);

    if (result.allDone) {
      doing = null;
      onDone(result.message);
    } else {
      problem = result.message;
      onKeepOnly(result.failed);
    }
  }

  const place = $derived(places.find((item) => item.id === placeId));
  const count = $derived(
    `${trays.length} tray${trays.length === 1 ? "" : "s"}`,
  );
</script>

<section
  class="many-trays form-card stack"
  aria-label="Work on the chosen trays"
>
  <div class="row many-trays-head">
    <strong>{count} chosen</strong>
    <button class="link-button" onclick={onClear}>Clear</button>
  </div>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  {#if doing === "ready"}
    <p>Mark {count} ready for sale?</p>
    <div class="row">
      <button
        disabled={busy}
        onclick={() =>
          forEachTray(
            (id) => work.ready(id),
            (n) => `Marked ${n} ready`,
          )}>Mark ready</button
      >
      <button class="secondary" onclick={() => (doing = null)}>Cancel</button>
    </div>
  {:else if doing === "move"}
    <label
      >Move {count} to
      <select bind:value={placeId}>
        {#each places as item (item.id)}
          <option value={item.id}>{item.label}</option>
        {/each}
      </select></label
    >
    <div class="row">
      <button
        disabled={busy || !placeId}
        onclick={() =>
          forEachTray(
            (id) => work.move(id, { locationId: placeId }),
            (n) => `Moved ${n} to ${place?.label ?? "the new place"}`,
          )}>Move them</button
      >
      <button class="secondary" onclick={() => (doing = null)}>Cancel</button>
    </div>
  {:else if doing === "treat"}
    <label>Product <input bind:value={product} autocomplete="off" /></label>
    <label>Dose <input bind:value={dose} autocomplete="off" /></label>
    <label
      >Area or crop treated <input
        bind:value={area}
        autocomplete="off"
      /></label
    >
    <div class="row">
      <button
        disabled={busy || !product.trim() || !dose.trim() || !area.trim()}
        onclick={() =>
          forEachTray(
            (id) =>
              work.treat(id, {
                product: product.trim(),
                dose: dose.trim(),
                area: area.trim(),
              }),
            (n) => `Recorded ${product.trim()} on ${n}`,
          )}>Record treatment</button
      >
      <button class="secondary" onclick={() => (doing = null)}>Cancel</button>
    </div>
  {:else}
    <div class="many-trays-actions">
      <button onclick={() => (doing = "ready")}>Mark ready</button>
      <button onclick={() => (doing = "move")}>Move</button>
      <button onclick={() => (doing = "treat")}>Treated</button>
      <button
        class="secondary"
        onclick={() => onLabels(trays.map((tray) => tray.labelCode))}
        >Print labels</button
      >
    </div>
  {/if}
  {#if busy}<p class="muted" role="status">Recording…</p>{/if}
</section>
