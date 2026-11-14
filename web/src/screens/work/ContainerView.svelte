<script lang="ts">
  import Pencil from "@lucide/svelte/icons/pencil";
  import Route from "@lucide/svelte/icons/route";
  import Undo2 from "@lucide/svelte/icons/undo-2";
  import Sparkles from "@lucide/svelte/icons/sparkles";
  import type { AskAbout } from "../../lib/assistant.svelte";
  import { crumbs } from "../../lib/crumbs.svelte";
  import { isOn } from "../../lib/features.svelte";
  import PrintButton from "../PrintButton.svelte";
  import BatchPhotos from "./BatchPhotos.svelte";
  import EditBatch from "./EditBatch.svelte";
  import DayPicker from "../DayPicker.svelte";
  import { chooseWorkDay, workDayFor } from "../../lib/workDays";
  import { isoDay, momentOn, onDay } from "../../lib/days";
  import Germinate from "./Germinate.svelte";
  import PlantCard from "./PlantCard.svelte";
  import { onMount } from "svelte";
  import { findPlaceByCode, isPlaceCode, type Place } from "../../lib/places";
  import { rememberedPlaces } from "../../lib/offline";
  import {
    focusHeading,
    onStepChange,
    pushStep,
    stepOf,
  } from "../../lib/steps";
  import { api } from "../../api/client";
  import type { Schemas } from "../../lib/types";
  import { rememberedContainer } from "../../lib/offline";
  import { recordWork } from "../../lib/queue";
  import { stageLabel, timeAtStage } from "../../lib/production";
  import {
    actionLabels,
    describeHistory,
    likelyNext,
    otherActions,
    whenWords,
    type HistoryEntry,
    type TrayAction,
  } from "../../lib/tray";
  import {
    isRecorded,
    outcomeProblem,
    plantName,
    work,
    type WorkRequest,
  } from "../../lib/work";
  import Count from "./Count.svelte";
  import Lose from "./Lose.svelte";
  import Move from "./Move.svelte";
  import Note from "./Note.svelte";
  import Passports from "./Passports.svelte";
  import Transfer from "./Transfer.svelte";
  import Treat from "./Treat.svelte";

  let {
    label,
    onTrace,
    onDone,
    onCancel,
    onNotFound,
    onAsk,
  }: {
    label: string;
    /** Opens Ask Hortiplanner about this tray, a plant in it or one of its photos, when this person can ask. */
    onAsk?: (about: AskAbout) => void;
    /** Only for the owner: opens the batch's trace report. */
    onTrace?: (batchId: string) => void;
    onDone: (message: string, eventId: string, newLabel?: string) => void;
    onCancel: () => void;
    onNotFound: (message: string) => void;
  } = $props();

  let container = $state<Schemas["ContainerResponse"] | null>(null);
  let history = $state<HistoryEntry[]>([]);
  let action = $state<TrayAction | null>(null);
  let problem = $state("");
  let busy = $state(false);
  let cardPlant = $state<string | null>(null);
  let editing = $state<string | null>(null);
  // The day work on this tray is recorded for: today, unless someone is writing up earlier work. A day chosen
  // is kept for this tray for a while, as each piece of work returns to Record and the tray is opened again.
  // svelte-ignore state_referenced_locally
  let workDay = $state(workDayFor(label));
  $effect(() => chooseWorkDay(label, workDay));
  let choosingDay = $state(false);
  const when = $derived(
    workDay === isoDay(new Date()) ? undefined : momentOn(workDay),
  );
  let saved = $state("");
  let fromDevice = $state(false);

  const now = new Date();
  const first = $derived(container ? likelyNext(container) : []);
  const rest = $derived(
    container
      ? otherActions(container).filter(
          (action) => action !== "passports" || isOn("Passports"),
        )
      : [],
  );
  // Each action and the plant card is a step in the browser's history, so Back returns to the tray.
  const back = () => window.history.back();

  // The tray, then whatever is open on it.
  crumbs(() => {
    const step =
      cardPlant !== null
        ? "About this plant"
        : editing !== null
          ? "Edit batch"
          : action
            ? actionLabels[action]
            : null;
    const tray = `Tray ${container?.labelCode ?? label}`;

    return step
      ? [{ label: tray, go: back }, { label: step }]
      : [{ label: tray }];
  });

  function openCard(plantId: string) {
    cardPlant = plantId;
    pushStep("trayCard", plantId);
  }

  function openEdit(batchId: string) {
    saved = "";
    editing = batchId;
    pushStep("trayEdit", batchId);
  }

  function editSaved(message: string) {
    saved = message;
    back();
    void load();
  }

  function stepped() {
    action = stepOf<TrayAction>("trayAction");
    cardPlant = stepOf<string>("trayCard");
    editing = stepOf<string>("trayEdit");
    void focusHeading();
  }

  // A USB scanner types a label then Enter. A place's label moves this tray there, without tapping Move.
  let typed = "";
  let lastKey = 0;

  async function scannedPlace(code: string) {
    if (!container) {
      return;
    }

    let places: Place[];

    try {
      places = (await api.GET("/api/locations")).data ?? [];
    } catch {
      places = rememberedPlaces() as Place[];
    }

    const place = findPlaceByCode(code, places);

    if (!place || place.kind === "Site" || place.archived) {
      problem = place
        ? `${place.name} is a whole site. Scan a bench or bed in it.`
        : `No place has the label ${code}.`;
      return;
    }

    if (place.id === container.locationId) {
      problem = `${container.labelCode} is already on ${place.path}.`;
      return;
    }

    await record(
      work.move(container.id, { locationId: place.id }, when),
      `moved to ${place.path}`,
    );
  }

  function keyed(event: KeyboardEvent) {
    const target = event.target as HTMLElement | null;

    if (
      action ||
      cardPlant ||
      editing ||
      target?.closest("input, textarea, select")
    ) {
      return;
    }

    const now = Date.now();
    typed = now - lastKey > 100 ? "" : typed;
    lastKey = now;

    if (event.key === "Enter") {
      if (isPlaceCode(typed)) {
        event.preventDefault();
        void scannedPlace(typed.trim());
      }

      typed = "";
    } else if (event.key.length === 1) {
      typed += event.key;
    }
  }

  async function load() {
    try {
      const { data } = await api.GET("/api/containers/by-label/{label}", {
        params: { path: { label } },
      });

      if (!data) {
        onNotFound(`No tray or pot has the label ${label}.`);
        return;
      }

      container = data;
      history =
        (
          await api.GET("/api/containers/{id}/history", {
            params: { path: { id: data.id } },
          })
        ).data ?? [];
    } catch {
      container = rememberedContainer(label);
      fromDevice = true;

      if (!container) {
        onNotFound(
          `There's no connection, and ${label} isn't saved on this device.`,
        );
      }
    }
  }

  onMount(() => {
    void load().then(focusHeading);
    window.addEventListener("keydown", keyed);
    const stop = onStepChange(stepped);

    return () => {
      stop();
      window.removeEventListener("keydown", keyed);
    };
  });

  async function record(item: WorkRequest, words: string) {
    busy = true;
    const outcome = await recordWork(item);
    busy = false;

    if (isRecorded(outcome)) {
      onDone(`${container?.labelCode} ${words}`, outcome.eventId);
    } else {
      problem = outcomeProblem(outcome);
    }
  }

  function choose(chosen: TrayAction) {
    problem = "";

    // On a tray nobody has counted, Germinated also offers a rough count.
    const uncounted = !!container?.contents.some((line) => line.notCounted);

    if (chosen === "germinated" && container && !uncounted) {
      void record(work.germinated(container.id, when), "germinated");
    } else if (chosen === "ready" && container) {
      void record(work.ready(container.id, when), "marked ready");
    } else {
      action = chosen;
      pushStep("trayAction", chosen);
      void focusHeading();
    }
  }

  async function undo(entry: HistoryEntry) {
    if (
      !confirm(
        `Undo "${describeHistory(entry)}"? It stays in the history, marked as undone.`,
      )
    ) {
      return;
    }

    busy = true;
    const outcome = await recordWork(work.undo(entry.eventId));
    busy = false;

    if (isRecorded(outcome)) {
      problem = "";
      await load();
    } else {
      problem = outcomeProblem(outcome);
    }
  }
</script>

{#if !container}
  <main><p class="muted">Loading…</p></main>
{:else if cardPlant}
  <PlantCard plantId={cardPlant} onBack={back}>
    {#snippet actions()}
      {#if onAsk}
        {@const plant = container!.contents.find(
          (line) => line.plantId === cardPlant,
        )}
        <button
          class="secondary"
          onclick={() =>
            onAsk({
              title: plant
                ? plantName(plant.commonName, plant.variety)
                : "this plant",
              plantId: cardPlant,
            })}><Sparkles size={18} /> Ask about this plant</button
        >
      {/if}
    {/snippet}
  </PlantCard>
{:else if editing}
  <EditBatch batchId={editing} onSaved={editSaved} onCancel={back} />
{:else if action === "lost"}
  <Lose {container} {when} {onDone} onCancel={back} />
{:else if action === "move"}
  <Move {container} {when} {onDone} onCancel={back} />
{:else if action === "potOn"}
  <Transfer {container} {when} type="PottedOn" {onDone} onCancel={back} />
{:else if action === "prickOut"}
  <Transfer {container} {when} type="PrickedOut" {onDone} onCancel={back} />
{:else if action === "germinated"}
  <Germinate {container} {when} {onDone} onCancel={back} />
{:else if action === "count"}
  <Count {container} {when} {onDone} onCancel={back} />
{:else if action === "treat"}
  <Treat {container} {when} {onDone} onCancel={back} />
{:else if action === "passports" && isOn("Passports")}
  <Passports {container} onCancel={back} />
{:else if action === "note"}
  <Note {container} {when} {onDone} onCancel={back} />
{:else}
  <main class="tray">
    <header class="plant-title">
      <h1>{container.labelCode}</h1>
      <p class="muted">
        {container.locationName ?? "No place recorded"}{container.potSizeName
          ? ` · ${container.potSizeName}`
          : ""}
      </p>
    </header>

    <div class="tray-layout">
      <section class="tray-details">
        {#if fromDevice}
          <p class="muted">
            There's no connection, so this is the tray as this device last saw
            it.
          </p>
        {/if}
        {#if container.stage && container.contents.length > 0}
          <ul class="tags">
            <li class="stage-tag">{stageLabel(container.stage)}</li>
            {#if container.stageSince}<li>
                for {timeAtStage(container.stageSince, now)}
              </li>{/if}
          </ul>
        {/if}

        {#each container.contents as line (line.batchId)}
          <div class="card tray-line">
            <p>
              {#if line.notCounted}
                <strong>Not counted yet:</strong>
              {:else}
                <strong class="tray-quantity"
                  >{line.rough
                    ? `about ${line.quantity}`
                    : line.quantity}</strong
                >
              {/if}
              {plantName(line.commonName, line.variety)}
            </p>
            <span class="muted credit">Batch {line.traceabilityCode}</span>
            {#if isOn("BatchPhotos")}
              <BatchPhotos
                batchId={line.batchId}
                containerId={container.id}
                kinds={fromDevice ? [] : ["Growing"]}
                onAsk={onAsk
                  ? (photoId) =>
                      onAsk({
                        title: `a photo of ${plantName(line.commonName, line.variety)}`,
                        photoId,
                        trayLabel: container!.labelCode,
                      })
                  : undefined}
              />
            {/if}
            <div class="row">
              <button class="link-button" onclick={() => openCard(line.plantId)}
                >About this plant</button
              >
              {#if !fromDevice}
                <button
                  class="link-button"
                  onclick={() => openEdit(line.batchId)}
                  ><Pencil size={16} aria-hidden="true" />Edit batch</button
                >
              {/if}
              {#if onTrace}
                <button
                  class="link-button"
                  onclick={() => onTrace(line.batchId)}
                  ><Route size={16} aria-hidden="true" />Trace this batch</button
                >
              {/if}
            </div>
          </div>
        {:else}
          <p class="note-empty muted">This tray is empty.</p>
        {/each}
      </section>

      <section class="tray-actions">
        {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
        {#if saved}<p class="card done" role="status">{saved}</p>{/if}
        <div class="work-day" class:earlier={!!when}>
          <span
            >Recording for <strong>{onDay(workDay).replace(/^on /, "")}</strong
            ></span
          >
          <button
            class="link-button"
            aria-expanded={choosingDay}
            onclick={() => (choosingDay = !choosingDay)}
            >{choosingDay ? "Done" : "Change"}</button
          >
        </div>
        {#if choosingDay}
          <DayPicker
            bind:value={workDay}
            label="When was this work done?"
            min={container.startedAt
              ? isoDay(new Date(container.startedAt))
              : null}
          />
          <p class="muted">
            Everything recorded on this tray is dated that day for the next half
            hour, or until you change it back to today.
          </p>
        {/if}
        <div class="next-actions">
          {#each first as item (item)}
            <button disabled={busy} onclick={() => choose(item)}
              >{actionLabels[item]}</button
            >
          {/each}
        </div>
        <div class="other-actions">
          {#each rest as item (item)}
            <button
              class="secondary"
              disabled={busy}
              onclick={() => choose(item)}>{actionLabels[item]}</button
            >
          {/each}
        </div>
        <PrintButton
          href="/api/labels/containers/{encodeURIComponent(
            container.labelCode,
          )}"
          label="Print label"
        />
        {#if onAsk}
          <button
            class="secondary"
            onclick={() =>
              onAsk({
                title: `tray ${container!.labelCode}`,
                trayLabel: container!.labelCode,
              })}><Sparkles size={18} /> Ask about this tray</button
          >
        {/if}
      </section>

      {#if history.length > 0}
        <section class="tray-history">
          <h2>What's happened</h2>
          <ol class="timeline">
            {#each history as entry (entry.eventId)}
              <li class:undone={entry.undone}>
                <span class="timeline-what">{describeHistory(entry)}</span>
                <span class="muted credit">
                  {whenWords(entry.occurredAt, now)}{entry.recordedBy
                    ? `, ${entry.recordedBy}`
                    : ""}{entry.undone ? " · undone" : ""}
                </span>
                {#if !entry.undone && entry.type !== "Sold" && !(entry.details as { forPassport?: boolean } | null)?.forPassport}
                  <button
                    class="link-button"
                    disabled={busy}
                    onclick={() => undo(entry)}
                    ><Undo2 size={16} aria-hidden="true" />Undo</button
                  >
                {/if}
              </li>
            {/each}
          </ol>
        </section>
      {/if}
    </div>
    <button class="back" onclick={onCancel}>Back</button>
  </main>
{/if}
