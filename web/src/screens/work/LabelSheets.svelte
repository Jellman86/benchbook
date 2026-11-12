<script lang="ts">
  import Printer from "@lucide/svelte/icons/printer";
  import { onDestroy, onMount } from "svelte";
  import { api } from "../../api/client";
  import { findTrays } from "../../lib/findTray";
  import {
    columnsOf,
    rememberedSheet,
    rememberSheet,
    sheetsNeeded,
    type SheetLayout,
  } from "../../lib/labelSheets";
  import { rememberedContainers } from "../../lib/offline";
  import { lineAmount } from "../../lib/sowing";
  import type { Schemas } from "../../lib/types";
  import { plantName } from "../../lib/work";

  // Labels on A4 sticker sheets from an ordinary printer, for a nursery without a label printer, or to label a
  // whole bench at once.
  let {
    onBack,
    trayLabels = [],
  }: {
    onBack: () => void;
    /** Trays already chosen, as when coming from Production with several picked. */
    trayLabels?: string[];
  } = $props();

  type What = "trays" | "places" | "plants";
  type Choice = { id: string; title: string; detail: string };

  let what = $state<What>("trays");
  let query = $state("");
  let trays = $state<Schemas["ContainerResponse"][]>(rememberedContainers());
  let places = $state<Schemas["LocationResponse"][]>([]);
  let plants = $state<Schemas["PlantResponse"][]>([]);
  let potSizes = $state<Schemas["PotSizeResponse"][]>([]);
  let potSizeId = $state("");
  let layouts = $state<SheetLayout[]>([]);
  let sheetId = $state(rememberedSheet() ?? "21");
  // svelte-ignore state_referenced_locally
  let chosen = $state<Record<string, boolean>>(
    Object.fromEntries(trayLabels.map((label) => [label, true])),
  );
  let startAt = $state(1);
  let copies = $state(1);
  let outlines = $state(false);
  let busy = $state(false);
  let problem = $state("");
  // The labels went to a new tab to print.
  let sent = $state(false);
  let preview = $state<string | null>(null);

  onMount(async () => {
    try {
      const [layoutList, trayList, placeList, plantList, sizes] =
        await Promise.all([
          api.GET("/api/labels/sheet-layouts"),
          api.GET("/api/containers/active"),
          api.GET("/api/locations"),
          api.GET("/api/plants"),
          api.GET("/api/pot-sizes"),
        ]);
      layouts = layoutList.data ?? [];
      trays = trayList.data ?? trays;
      places = (placeList.data ?? []).filter((place) => place.kind !== "Site");
      plants = (plantList.data ?? []).filter((plant) => !plant.archived);
      potSizes = sizes.data ?? [];

      if (!layouts.some((layout) => layout.id === sheetId)) {
        sheetId = layouts[0]?.id ?? "21";
      }
    } catch {
      problem = "Label sheets need a connection.";
    }
  });

  onDestroy(clearPdf);

  const sheet = $derived(layouts.find((layout) => layout.id === sheetId));
  const perSheet = $derived(Number(sheet?.perSheet ?? 21));

  /** What can be printed, filtered by what was typed. */
  const choices = $derived.by((): Choice[] => {
    const words = query.trim().toLowerCase();
    const matches = (text: string) =>
      !words || text.toLowerCase().includes(words);

    if (what === "trays") {
      const shown = words ? findTrays(trays, query) : trays;
      return shown.map((tray) => ({
        id: tray.labelCode,
        title: `${tray.labelCode} · ${tray.contents
          .map(
            (line) =>
              `${lineAmount(line)} ${plantName(line.commonName, line.variety)}`,
          )
          .join(", ")}`,
        detail: tray.locationName ?? "No place recorded",
      }));
    }

    if (what === "places") {
      return places
        .filter((place) => matches(`${place.path} ${place.labelCode}`))
        .map((place) => ({
          id: place.id,
          title: place.path,
          detail: place.labelCode,
        }));
    }

    return plants
      .filter((plant) =>
        matches(
          `${plant.commonName} ${plant.variety ?? ""} ${plant.botanicalName ?? ""}`,
        ),
      )
      .map((plant) => ({
        id: plant.id,
        title: plantName(plant.commonName, plant.variety),
        detail: plant.botanicalName ?? "",
      }));
  });

  const picked = $derived(
    Object.entries(chosen)
      .filter(([, on]) => on)
      .map(([id]) => id),
  );
  const labelCount = $derived(picked.length * copies);
  const pages = $derived(sheetsNeeded(labelCount, perSheet, startAt));

  function switchTo(next: What) {
    what = next;
    chosen = {};
    query = "";
    clearPdf();
  }

  function clearPdf() {
    if (preview) {
      URL.revokeObjectURL(preview);
    }

    sent = false;
    preview = null;
  }

  function chooseShown(on: boolean) {
    const next = { ...chosen };

    for (const choice of choices) {
      next[choice.id] = on;
    }

    chosen = next;
    clearPdf();
  }

  const kind = $derived(
    what === "trays"
      ? "ContainerLabel"
      : what === "places"
        ? "PlaceLabel"
        : "PlantLabel",
  );
  const request = $derived({
    kind,
    targets: picked,
    sheet: sheetId,
    startAt,
    copies,
    outlines,
    potSizeId: what === "plants" && potSizeId ? potSizeId : null,
  });

  // A picture of the first sheet, to check before printing.
  async function showFirstSheet() {
    busy = true;
    problem = "";
    clearPdf();
    rememberSheet(sheetId);

    try {
      const response = await fetch("/api/labels/sheets", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ ...request, picture: true }),
      });

      if (response.ok) {
        preview = URL.createObjectURL(await response.blob());
      } else {
        problem = sheetProblem(response.status);
      }
    } catch {
      problem = "Label sheets need a connection.";
    } finally {
      busy = false;
    }
  }

  // The PDF to print, sent as an ordinary form into a new tab: a fetched PDF opened as a blob comes up blank
  // from an iPhone's home-screen app.
  function makeLabels() {
    rememberSheet(sheetId);
    const form = document.createElement("form");
    form.method = "post";
    form.action = "/api/labels/sheets/print";
    form.target = "_blank";
    const field = document.createElement("input");
    field.type = "hidden";
    field.name = "request";
    field.value = JSON.stringify(request);
    form.append(field);
    document.body.append(form);
    form.submit();
    form.remove();
    sent = true;
  }

  const sheetProblem = (status: number) =>
    status === 401
      ? "You've been signed out. Sign in again, then make the labels."
      : status === 404
        ? "One of those no longer exists. Go back and choose again."
        : "Those labels couldn't be made. Please try again.";
</script>

<main class="label-sheets">
  <h1>Print labels on A4</h1>
  <p class="muted">
    For A4 sticker sheets in an ordinary printer: no label printer needed.
  </p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  <div class="segmented">
    <button aria-pressed={what === "trays"} onclick={() => switchTo("trays")}
      >Trays</button
    >
    <button aria-pressed={what === "places"} onclick={() => switchTo("places")}
      >Places</button
    >
    <button aria-pressed={what === "plants"} onclick={() => switchTo("plants")}
      >Plants</button
    >
  </div>

  <label
    >Find <input
      type="search"
      bind:value={query}
      autocomplete="off"
      placeholder={what === "trays" ? "Plant, place or code" : "Name"}
    /></label
  >
  <div class="row">
    <button class="secondary" onclick={() => chooseShown(true)}
      >Choose all {choices.length} shown</button
    >
    <button class="secondary" onclick={() => chooseShown(false)}>Clear</button>
  </div>

  <ul class="sheet-choices" aria-label="What to print">
    {#each choices as choice (choice.id)}
      <li>
        <label class="tick">
          <input
            type="checkbox"
            checked={!!chosen[choice.id]}
            onchange={(event) => {
              chosen = { ...chosen, [choice.id]: event.currentTarget.checked };
              clearPdf();
            }}
          />
          <span class="plant-names">
            <strong>{choice.title}</strong>
            {#if choice.detail}<em>{choice.detail}</em>{/if}
          </span>
        </label>
      </li>
    {:else}
      <p class="muted">Nothing to show.</p>
    {/each}
  </ul>

  <div class="form-card stack">
    <label
      >Sticker sheet <select
        bind:value={sheetId}
        onchange={() => {
          startAt = 1;
          clearPdf();
        }}
      >
        {#each layouts as layout (layout.id)}
          <option value={layout.id}>{layout.name}</option>
        {/each}
      </select></label
    >

    {#if what === "plants" && potSizes.length > 0}
      <label
        >Price for <select bind:value={potSizeId} onchange={clearPdf}>
          <option value="">No price</option>
          {#each potSizes as size (size.id)}
            <option value={size.id}>{size.name}</option>
          {/each}
        </select></label
      >
    {/if}

    {#if sheet}
      <label class="sheet-start"
        >Start at sticker <input
          type="number"
          inputmode="numeric"
          min="1"
          max={perSheet}
          value={startAt}
          onchange={(event) => {
            startAt = Math.min(
              Math.max(Math.round(Number(event.currentTarget.value)) || 1, 1),
              perSheet,
            );
            event.currentTarget.value = String(startAt);
            clearPdf();
          }}
        /></label
      >
      <p class="muted">Or tap the first free one on a used sheet.</p>
      <div
        class="sheet-picture"
        style:--columns={columnsOf(sheet)}
        role="group"
        aria-label="Stickers on the sheet"
      >
        {#each Array.from({ length: perSheet }, (_, index) => index + 1) as number (number)}
          <button
            class:used={number < startAt}
            aria-pressed={number === startAt}
            aria-label="Start at sticker {number}"
            onclick={() => {
              startAt = number;
              clearPdf();
            }}
          ></button>
        {/each}
      </div>
    {/if}

    <label
      >Copies of each <input
        type="number"
        min="1"
        max="10"
        inputmode="numeric"
        bind:value={copies}
        onchange={clearPdf}
      /></label
    >
    <label class="tick">
      <input type="checkbox" bind:checked={outlines} onchange={clearPdf} />
      Plain paper: draw lines to cut along
    </label>
  </div>

  <p class="muted">
    {labelCount === 0
      ? "Choose something to print."
      : `${labelCount} label${labelCount === 1 ? "" : "s"} on ${pages} sheet${pages === 1 ? "" : "s"}.`}
    Print at actual size (100%), not "fit to page".
  </p>

  {#if preview}
    <img
      class="sheet-preview"
      src={preview}
      alt="The first sheet of labels, as it will print"
    />
  {/if}

  {#if labelCount > 300}
    <p class="problem">
      That's {labelCount} labels: at most 300 can be made at once. Choose fewer, or
      fewer copies of each.
    </p>
  {/if}
  {#if sent}
    <p class="card done" role="status">
      <Printer size={18} /> The labels open in a new tab, ready to print. If nothing
      opened, allow pop-ups for GrowLog and make them again.
    </p>
  {/if}
  <div class="row">
    <button
      disabled={busy || labelCount === 0 || labelCount > 300}
      onclick={makeLabels}>Make the labels</button
    >
    <button
      class="secondary"
      disabled={busy || labelCount === 0 || labelCount > 300}
      onclick={showFirstSheet}
      >{busy ? "Drawing the sheet…" : "Show the first sheet"}</button
    >
  </div>

  <button class="back" onclick={onBack}>Back</button>
</main>
