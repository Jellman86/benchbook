<script lang="ts">
  import { lineAmount } from "../../lib/sowing";
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
  import { signal } from "../../lib/feedback";
  import { isRecorded, outcomeProblem, plantName, work } from "../../lib/work";
  import Camera from "./Camera.svelte";

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
  let camera = $state(false);
  const cameraAvailable = !!navigator.mediaDevices?.getUserMedia;

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
          `${lineAmount(line)} ${plantName(line.commonName, line.variety)}`,
      )
      .join(", ");
  }

  function scanned(event: SubmitEvent) {
    event.preventDefault();
    const label = code.trim();
    code = "";
    void take(label);
  }

  /** One scanned label, from the scanner box or the camera. The box is always ready for the next. */
  async function take(label: string) {
    problem = "";

    if (!label) {
      return;
    }

    try {
      busy = true;
      const ok = await moveOne(label);
      signal(ok);
    } finally {
      busy = false;
      await tick();
      scanBox?.focus();
    }
  }

  async function moveOne(label: string): Promise<boolean> {
    // Scanning a place's label chooses it, at any point.
    if (isPlaceCode(label)) {
      const place = findPlaceByCode(label, places);

      if (!place) {
        problem = `No place has the label ${label}.`;
      } else if (place.kind === "Site") {
        problem = `${place.name} is a whole site. Scan a bench or bed in it.`;
      } else {
        target = place.id;
      }

      return !!place && place.kind !== "Site";
    }

    if (!chosen) {
      problem = "Choose or scan where they're going first.";
      return false;
    }

    const tray = await findTray(label);

    if (!tray) {
      problem = `No tray has the label ${label}.`;
      return false;
    }

    const outcome = await recordWork(
      work.move(tray.id, { locationId: chosen.id }),
    );

    if (!isRecorded(outcome)) {
      problem = outcomeProblem(outcome);
      return false;
    }

    moved = [
      {
        label: tray.labelCode,
        what: describe(tray),
        queued: outcome.kind === "queued",
      },
      ...moved,
    ];
    return true;
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

  {#if camera}
    <Camera
      continuous
      onScan={(scannedCode) => void take(scannedCode)}
      onCancel={() => (camera = false)}
    />
  {:else if cameraAvailable}
    <button class="secondary" onclick={() => (camera = true)}>Use camera</button
    >
  {/if}

  <button class="secondary" onclick={onFinished}>Finished moving</button>
</main>
