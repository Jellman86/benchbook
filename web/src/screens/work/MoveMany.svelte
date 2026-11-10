<script lang="ts">
  import { onMount, tick } from "svelte";
  import { api } from "../../api/client";
  import { rememberedContainer, rememberedPlaces } from "../../lib/offline";
  import {
    findPlaceByCode,
    isPlaceCode,
    trayPlaces,
    type Place,
  } from "../../lib/places";
  import { recordWork } from "../../lib/queue";
  import type { Schemas } from "../../lib/types";
  import { isRecorded, outcomeProblem, plantName, work } from "../../lib/work";

  // Choose the place once, then scan tray after tray.
  let {
    destination = null,
    onFinished,
  }: { destination?: string | null; onFinished: () => void } = $props();

  let places = $state<Place[]>([]);
  // svelte-ignore state_referenced_locally
  let target = $state<string | null>(destination);
  let code = $state("");
  let problem = $state("");
  let busy = $state(false);
  let moved = $state<{ label: string; what: string; queued: boolean }[]>([]);
  let scanBox = $state<HTMLInputElement | null>(null);

  const options = $derived(trayPlaces(places));
  const chosen = $derived(options.find((option) => option.id === target));

  onMount(async () => {
    try {
      places = (await api.GET("/api/locations")).data ?? [];
    } catch {
      places = rememberedPlaces() as Place[];
    }

    await tick();
    scanBox?.focus();
  });

  async function findTray(label: string) {
    try {
      const { data } = await api.GET("/api/containers/by-label/{label}", {
        params: { path: { label } },
      });
      return data ?? null;
    } catch {
      return rememberedContainer(label);
    }
  }

  function describe(tray: Schemas["ContainerResponse"]) {
    return tray.contents
      .map(
        (line) =>
          `${line.quantity} ${plantName(line.commonName, line.variety)}`,
      )
      .join(", ");
  }

  async function scanned(event: SubmitEvent) {
    event.preventDefault();
    const label = code.trim();
    code = "";
    problem = "";

    if (!label) {
      return;
    }

    // Scanning a place's label chooses it, at any point.
    if (isPlaceCode(label)) {
      const place = findPlaceByCode(label, places);
      target = place && place.kind !== "Site" ? place.id : target;
      problem = place ? "" : `No place has the label ${label}.`;
      return;
    }

    if (!chosen) {
      problem = "Choose or scan where they're going first.";
      return;
    }

    busy = true;
    const tray = await findTray(label);

    if (!tray) {
      busy = false;
      problem = `No tray has the label ${label}.`;
      return;
    }

    const outcome = await recordWork(
      work.move(tray.id, { locationId: chosen.id }),
    );
    busy = false;

    if (isRecorded(outcome)) {
      moved = [
        {
          label: tray.labelCode,
          what: describe(tray),
          queued: outcome.kind === "queued",
        },
        ...moved,
      ];
    } else {
      problem = outcomeProblem(outcome);
    }

    await tick();
    scanBox?.focus();
  }
</script>

<main>
  <h1>Move several</h1>

  <label>
    Going to
    <select bind:value={target}>
      <option value={null}>Choose, or scan the place's label…</option>
      {#each options as option (option.id)}
        <option value={option.id}>{option.label}</option>
      {/each}
    </select>
  </label>

  <form class="stack" onsubmit={scanned}>
    <label>
      {chosen ? `Scan each tray going to ${chosen.label}` : "Scan a label"}
      <input
        bind:this={scanBox}
        bind:value={code}
        autocomplete="off"
        autocapitalize="characters"
        disabled={busy}
      />
    </label>
    <button type="submit" disabled={busy}>Move it</button>
  </form>

  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  {#if moved.length > 0}
    <p class="card done" role="status">
      <strong class="done-heading"
        >Moved {moved.length} to {chosen?.label ?? "the place chosen"}</strong
      >
    </p>
    <ol class="timeline">
      {#each moved as item, index (`${item.label}-${index}`)}
        <li>
          <span class="timeline-what">{item.label}: {item.what}</span>
          <span class="muted credit"
            >{item.queued ? "Saved on this device for now" : "Moved"}</span
          >
        </li>
      {/each}
    </ol>
  {/if}

  <button onclick={onFinished}>Finished moving</button>
</main>
