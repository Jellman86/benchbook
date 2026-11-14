<script lang="ts">
  import PlaceMiniMap from "../manage/PlaceMiniMap.svelte";
  import { crumbs } from "../../lib/crumbs.svelte";
  import { isOn } from "../../lib/features.svelte";
  import { lineAmount } from "../../lib/sowing";
  import PrintButton from "../PrintButton.svelte";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { stopUsing } from "../../lib/archive";
  import { stageLabel } from "../../lib/production";
  import {
    kindLabel,
    placeKinds,
    possibleParents,
    byContainer,
    trayPlaces,
    withEverythingInside,
    type Place,
    type AddedPlace,
  } from "../../lib/places";
  import { recordWork } from "../../lib/queue";
  import { attempt, type Schemas } from "../../lib/types";
  import { isRecorded, plantName, work } from "../../lib/work";
  import AddPlaces from "../manage/AddPlaces.svelte";
  import EntryPhotos from "../manage/EntryPhotos.svelte";
  import Loading from "./Loading.svelte";

  let {
    placeId,
    canManage = false,
    onBack,
    onOpenTray,
    onOpenPlace,
    onMoveTraysHere,
    onDrawOnMap,
  }: {
    placeId: string;
    canManage?: boolean;
    /** Opens the map to draw a place, when the places map is on. */
    onDrawOnMap?: (place: AddedPlace) => void;
    onBack: () => void;
    onOpenTray: (label: string) => void;
    onOpenPlace: (id: string) => void;
    onMoveTraysHere: (id: string) => void;
  } = $props();

  type Row = Schemas["ProductionRow"];

  let places = $state<Place[]>([]);
  let rows = $state<Row[]>([]);
  let loaded = $state(false);
  let problem = $state("");
  let message = $state("");
  let busy = $state(false);
  let task = $state<"move" | "moveAll" | "rename" | "kind" | "add" | null>(
    null,
  );
  let newKind = $state<Schemas["LocationKind"]>("Bench");
  let newName = $state("");
  let newParent = $state("");
  let destination = $state("");

  const place = $derived(places.find((item) => item.id === placeId));

  // The place and the places it sits in, each one tap away.
  crumbs(() => {
    const path: Place[] = [];
    let at = place;

    while (at && path.length < 20) {
      path.unshift(at);
      const parentId = at.parentId;
      at = parentId ? places.find((item) => item.id === parentId) : undefined;
    }

    return path.map((item) => ({
      label: item.name,
      go: item.id === placeId ? undefined : () => onOpenPlace(item.id),
    }));
  });
  const inside = $derived(
    places
      .filter((item) => item.parentId === placeId && !item.archived)
      .sort((a, b) => a.name.localeCompare(b.name, "en-GB", { numeric: true })),
  );
  const here = $derived(withEverythingInside(placeId, places));
  const trays = $derived(
    byContainer(
      rows.filter((row) => row.locationId && here.has(row.locationId)),
    ),
  );
  const plantCount = $derived(
    trays.flat().reduce((total, row) => total + Number(row.quantity), 0),
  );
  const destinations = $derived(
    trayPlaces(places).filter((option) => !here.has(option.id)),
  );

  const setProblem = (text: string) => (problem = text);

  async function load() {
    places = (await api.GET("/api/locations")).data ?? [];
    rows = (await api.GET("/api/production")).data ?? [];
    loaded = true;
  }

  onMount(() => void attempt(load, setProblem));

  const rename = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.PUT("/api/locations/{id}", {
        params: { path: { id: placeId } },
        body: { name: newName },
      });
      problem = response.ok
        ? ""
        : "There's already a place with that name there.";
      task = response.ok ? null : task;
      await load();
    }, setProblem);
  };

  const moveInto = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.PUT("/api/locations/{id}/parent", {
        params: { path: { id: placeId } },
        body: { parentId: newParent },
      });
      problem = response.ok
        ? ""
        : response.status === 409
          ? "There's already a place with that name there."
          : "It can't go there.";
      task = response.ok ? null : task;
      await load();
    }, setProblem);
  };

  let lastMove = $state<string[]>([]);

  /** Moves every tray here, and in the places inside, to one other place, saying what didn't move. */
  async function moveEverything() {
    const target = destinations.find((option) => option.id === destination);

    if (
      !target ||
      !confirm(
        `Move all ${trays.length} trays and pot groups from here to ${target.label}?`,
      )
    ) {
      return;
    }

    busy = true;
    const done: string[] = [];
    const failedLabels: string[] = [];

    for (const lines of trays) {
      const outcome = await recordWork(
        work.move(lines[0].containerId, { locationId: target.id }),
      );

      if (isRecorded(outcome)) {
        done.push(outcome.eventId);
      } else {
        failedLabels.push(lines[0].labelCode);
      }
    }

    busy = false;
    task = null;
    lastMove = done;
    message = `Moved ${done.length} to ${target.label}.`;
    problem =
      failedLabels.length > 0
        ? `These didn't move: ${failedLabels.join(", ")}.`
        : "";
    await load();
  }

  async function undoMove() {
    busy = true;

    for (const eventId of lastMove) {
      await recordWork(work.undo(eventId));
    }

    busy = false;
    message = `Undone: ${lastMove.length} moved back.`;
    lastMove = [];
    await load();
  }

  // A site stands on its own, so anything that isn't one needs a place to sit in when it has none yet.
  const needsParent = $derived(
    !!place && newKind !== "Site" && !place.parentId,
  );

  const changeKind = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.PUT("/api/locations/{id}/kind", {
        params: { path: { id: placeId } },
        body: { kind: newKind, parentId: needsParent ? newParent : null },
      });
      problem = response.ok
        ? ""
        : response.status === 409
          ? "There's already a place with that name there."
          : "It can't go there.";
      task = response.ok ? null : task;
      await load();
    }, setProblem);
  };

  const whyKept: Record<string, string> = {
    HasPlaces:
      "It has places inside it. Move or delete those first, or mark it no longer used.",
    HasWork:
      "Work has been recorded there, so it's kept for the records. Mark it no longer used instead.",
    HasSensors:
      "A sensor is or was there, so it's kept for its readings. Mark it no longer used instead.",
    HasRecords:
      "Mother plants or jobs refer to it, so it's kept. Mark it no longer used instead.",
  };

  const remove = () =>
    attempt(async () => {
      if (
        !place ||
        !confirm(
          `Delete ${place.name} for good? Only a place added by mistake, with nothing ever recorded there, can be deleted.`,
        )
      ) {
        return;
      }

      const { response, error } = await api.DELETE("/api/locations/{id}", {
        params: { path: { id: place.id } },
      });

      if (response.ok) {
        onBack();
      } else {
        problem =
          whyKept[error?.problem ?? ""] ??
          "It couldn't be deleted. Please try again.";
      }
    }, setProblem);

  const retire = () =>
    attempt(async () => {
      if (!place) {
        return;
      }

      const refused = await stopUsing("locations", place.id, place.name);

      if (refused) {
        problem = refused;
      } else {
        onBack();
      }
    }, setProblem);
</script>

<main>
  {#if !loaded}
    <Loading text="Opening this place…" />
  {:else if !place}
    <p class="problem" role="alert">That place couldn't be found.</p>
  {:else}
    <header class="plant-title">
      <h1>{place.name}</h1>
      <p class="muted">
        {kindLabel(place.kind)}{place.path !== place.name
          ? ` in ${place.path.slice(0, place.path.lastIndexOf(","))}`
          : ""} · {place.labelCode}
      </p>
    </header>

    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    {#if message}
      <div class="card done" role="status">
        <strong class="done-heading">{message}</strong>
        {#if lastMove.length > 0}
          <button class="secondary" disabled={busy} onclick={undoMove}
            >Undo this move</button
          >
        {/if}
      </div>
    {/if}

    <div class="plant-layout">
      <aside class="plant-side">
        {#if canManage && isOn("PlacesMap")}
          {#key place.id}
            <PlaceMiniMap
              {place}
              {places}
              onDraw={onDrawOnMap
                ? () =>
                    onDrawOnMap({
                      id: place!.id,
                      name: place!.name,
                      kind: place!.kind,
                      parentId: place!.parentId ?? null,
                    })
                : undefined}
            />
          {/key}
        {/if}

        <section class="stack tight" aria-label="Photos">
          {#if place.photoIds.length > 0}
            <h2 class="form-heading">Photos</h2>
          {/if}
          <EntryPhotos
            entry="Location"
            id={place.id}
            photoIds={place.photoIds as string[]}
            gallery
            onChanged={load}
            onProblem={setProblem}
          />
        </section>

        <dl class="facts card">
          <dt>Trays and pots</dt>
          <dd>{trays.length}</dd>
          <dt>Plants</dt>
          <dd>{plantCount}</dd>
          <dt>Places inside</dt>
          <dd>{inside.length}</dd>
        </dl>

        <div class="stack">
          {#if place.kind !== "Site"}
            <button onclick={() => onMoveTraysHere(place!.id)}
              >Move trays here</button
            >
          {/if}
          {#if trays.length > 0}
            <button
              class="secondary"
              onclick={() => (task = task === "moveAll" ? null : "moveAll")}
              >Move all of this somewhere else…</button
            >
          {/if}
          {#if task === "moveAll"}
            <div class="card stack">
              <label>
                Move all {trays.length} to
                <select bind:value={destination}>
                  <option value="">Choose…</option>
                  {#each destinations as option (option.id)}
                    <option value={option.id}>{option.label}</option>
                  {/each}
                </select>
              </label>
              <button disabled={busy || !destination} onclick={moveEverything}
                >Move them</button
              >
            </div>
          {/if}
          <PrintButton
            href="/api/labels/places/{place.id}"
            label="Print place label"
          />
        </div>

        {#if canManage}
          <div class="row plant-actions">
            <button
              class="secondary"
              onclick={() => {
                task = "rename";
                newName = place!.name;
              }}>Rename</button
            >
            {#if place.kind !== "Site"}
              <button
                class="secondary"
                onclick={() => {
                  task = "move";
                  newParent = place!.parentId ?? "";
                }}>Move into…</button
              >
            {/if}
            <button class="secondary" onclick={() => (task = "add")}
              >Add inside</button
            >
            <button
              class="secondary"
              onclick={() => {
                task = "kind";
                newKind = place!.kind;
                newParent = place!.parentId ?? "";
              }}>Change type</button
            >
            <button class="secondary" onclick={retire}>No longer used</button>
            <button class="secondary destructive" onclick={remove}
              >Delete</button
            >
          </div>
          {#if task === "rename"}
            <form class="card stack" onsubmit={rename}>
              <label>Name <input bind:value={newName} required /></label>
              <div class="row">
                <button type="submit">Save</button>
                <button
                  type="button"
                  class="secondary"
                  onclick={() => (task = null)}>Cancel</button
                >
              </div>
            </form>
          {:else if task === "kind"}
            <form class="card stack" onsubmit={changeKind}>
              <label>
                What sort of place it is
                <select bind:value={newKind}>
                  {#each placeKinds as option (option.kind)}
                    <option value={option.kind}>{option.label}</option>
                  {/each}
                </select>
              </label>
              {#if needsParent}
                <label>
                  Put it inside
                  <select bind:value={newParent} required>
                    <option value="" disabled>Choose…</option>
                    {#each possibleParents(place.id, places) as option (option.id)}
                      <option value={option.id}>{option.path}</option>
                    {/each}
                  </select>
                </label>
              {:else if newKind === "Site" && place.parentId}
                <p class="muted">
                  A site stands on its own, so it will no longer sit inside
                  another place. What's inside it stays put.
                </p>
              {/if}
              <p class="muted">
                Its label number stays the same, so labels already printed still
                work.
              </p>
              <div class="row">
                <button type="submit" disabled={newKind === place.kind}
                  >Save</button
                >
                <button
                  type="button"
                  class="secondary"
                  onclick={() => (task = null)}>Cancel</button
                >
              </div>
            </form>
          {:else if task === "move"}
            <form class="card stack" onsubmit={moveInto}>
              <label>
                Put it inside
                <select bind:value={newParent} required>
                  {#each possibleParents(place.id, places) as option (option.id)}
                    <option value={option.id}>{option.path}</option>
                  {/each}
                </select>
              </label>
              <div class="row">
                <button type="submit">Move it</button>
                <button
                  type="button"
                  class="secondary"
                  onclick={() => (task = null)}>Cancel</button
                >
              </div>
            </form>
          {:else if task === "add"}
            <div class="card">
              <AddPlaces
                {places}
                inside={place.id}
                onAdded={async (added) => {
                  task = null;
                  await load();

                  if (added && onDrawOnMap && isOn("PlacesMap")) {
                    onDrawOnMap(added);
                  }
                }}
              />
            </div>
          {/if}
        {/if}
      </aside>

      <div class="plant-main">
        {#if inside.length > 0}
          <section class="stack">
            <h2>Inside</h2>
            <div class="card-grid">
              {#each inside as child (child.id)}
                <button
                  class="plant-card"
                  onclick={() => onOpenPlace(child.id)}
                >
                  <span class="plant-names">
                    <strong>{child.name}</strong>
                    <em>{kindLabel(child.kind)}</em>
                  </span>
                </button>
              {/each}
            </div>
          </section>
        {/if}

        <section class="stack">
          <h2>What's here</h2>
          {#each trays as lines (lines[0].containerId)}
            <button
              class="plant-card"
              onclick={() => onOpenTray(lines[0].labelCode)}
            >
              <span class="plant-names">
                <strong>{lines[0].labelCode}</strong>
                {#each lines as line (line.batchId)}
                  <span
                    >{lineAmount(line)}
                    {plantName(line.commonName, line.variety)} · {stageLabel(
                      line.stage,
                    )}</span
                  >
                {/each}
                {#if lines[0].locationId !== place.id}
                  <em>{lines[0].locationName}</em>
                {/if}
              </span>
            </button>
          {:else}
            <p class="note-empty muted">
              Nothing is growing here at the moment.
            </p>
          {/each}
        </section>
      </div>
    </div>
  {/if}

  <button class="back" onclick={onBack}>Back</button>
</main>
