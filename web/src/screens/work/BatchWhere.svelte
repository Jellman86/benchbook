<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { rememberedPlaces } from "../../lib/offline";
  import { trayPlaces, type Place } from "../../lib/places";
  import { recordWork } from "../../lib/queue";
  import type { Schemas } from "../../lib/types";
  import { isRecorded, outcomeProblem, work } from "../../lib/work";

  // Where a batch's trays are, with a move for each, so a batch can be moved from Edit batch as well as from its
  // tray. A move is recorded straight away, like Move on the tray, not with the batch's other changes.
  let { batchId }: { batchId: string } = $props();

  type Tray = Pick<
    Schemas["ProductionRow"],
    "containerId" | "labelCode" | "locationId" | "locationName"
  >;

  let trays = $state<Tray[]>([]);
  let places = $state<{ id: string; label: string }[]>([]);
  let moving = $state<string | null>(null);
  let destination = $state("");
  let busy = $state(false);
  let message = $state("");
  let problem = $state("");

  const placeName = (tray: Tray) =>
    places.find((place) => place.id === tray.locationId)?.label ??
    tray.locationName ??
    "No place recorded";

  async function loadTrays() {
    const rows = (await api.GET("/api/production")).data ?? [];

    // One entry per tray, however many lines of this batch it holds.
    trays = rows.filter(
      (row, index) =>
        row.batchId === batchId &&
        rows.findIndex(
          (other) =>
            other.batchId === batchId && other.containerId === row.containerId,
        ) === index,
    );
  }

  onMount(async () => {
    try {
      places = trayPlaces(
        (await api.GET("/api/locations")).data ?? ([] as Place[]),
      );
      await loadTrays();
    } catch {
      places = trayPlaces(rememberedPlaces() as Place[]);
    }
  });

  async function move(tray: Tray) {
    const place = places.find((item) => item.id === destination);

    if (!place) {
      problem = "Choose where it's going.";
      return;
    }

    busy = true;
    problem = "";
    const outcome = await recordWork(
      work.move(tray.containerId, { locationId: place.id }),
    );
    busy = false;

    if (isRecorded(outcome)) {
      message = `Moved ${tray.labelCode} to ${place.label}.`;
      moving = null;
      destination = "";

      try {
        await loadTrays();
      } catch {
        // The move is saved; the list catches up when there's a signal.
      }
    } else {
      problem = outcomeProblem(outcome);
    }
  }
</script>

{#if trays.length > 0}
  <section class="stack tight" aria-labelledby="batch-where">
    <h2 class="form-heading" id="batch-where">Where it is</h2>
    {#if message}<p class="card done" role="status">{message}</p>{/if}
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    <ul class="plain-list stack tight">
      {#each trays as tray (tray.containerId)}
        <li class="card stack tight">
          <span><strong>{tray.labelCode}</strong> · {placeName(tray)}</span>
          {#if moving === tray.containerId}
            <label
              >Move to <select bind:value={destination}>
                <option value="">Choose…</option>
                {#each places.filter((place) => place.id !== tray.locationId) as place (place.id)}
                  <option value={place.id}>{place.label}</option>
                {/each}
              </select></label
            >
            <div class="row">
              <button
                type="button"
                disabled={busy || !destination}
                onclick={() => move(tray)}>Move it</button
              >
              <button
                type="button"
                class="secondary"
                onclick={() => {
                  moving = null;
                  destination = "";
                  problem = "";
                }}>Cancel</button
              >
            </div>
          {:else}
            <button
              type="button"
              class="secondary"
              onclick={() => {
                moving = tray.containerId;
                destination = "";
                message = "";
              }}>Move</button
            >
          {/if}
        </li>
      {/each}
    </ul>
    <p class="muted">A move is saved straight away.</p>
  </section>
{/if}
