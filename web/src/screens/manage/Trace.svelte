<script lang="ts">
  import Printer from "@lucide/svelte/icons/printer";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { dayLabel, momentLabel } from "../../lib/dates";
  import {
    containerOf,
    describeStep,
    passportStatusLabels,
    sourceLabels,
    stillGrowing,
    type BatchTrace,
  } from "../../lib/trace";
  import { attempt, type Schemas } from "../../lib/types";
  import { plantName } from "../../lib/work";

  // Trace-back and trace-forward for one batch: what a plant health inspector asks to see.
  let {
    batchId,
    onChoose,
    onBack,
    onOpenTray,
  }: {
    batchId: string | null;
    onChoose: (id: string | null) => void;
    onBack: () => void;
    onOpenTray: (label: string) => void;
  } = $props();

  let query = $state("");
  let matches = $state<Schemas["TraceMatch"][]>([]);
  let searched = $state(false);
  let loaded = $state(false);
  let trace = $state<BatchTrace | null>(null);
  let problem = $state("");

  const now = new Date();
  const setProblem = (message: string) => (problem = message);

  const find = (event?: SubmitEvent) => {
    event?.preventDefault();

    return attempt(async () => {
      matches =
        (await api.GET("/api/trace", { params: { query: { q: query } } }))
          .data ?? [];
      searched = query.trim().length > 0;
      loaded = true;
    }, setProblem);
  };

  onMount(() => void find());

  $effect(() => {
    const id = batchId;
    trace = null;

    if (id) {
      void attempt(async () => {
        const { data, response } = await api.GET("/api/batches/{id}/trace", {
          params: { path: { id } },
        });
        trace = data ?? null;
        problem = response.ok ? "" : "That batch couldn't be found.";
      }, setProblem);
    }
  });

  const xylella = (host: boolean | null) =>
    host === true ? "Yes" : host === false ? "No" : "Not checked";

  const origin = $derived.by(() => {
    if (!trace) {
      return [];
    }

    const rows: [string, string][] = [
      ["How it started", sourceLabels[trace.source]],
      ["Started", dayLabel(trace.startedAt.slice(0, 10), now)],
      ["Country of origin", trace.originCountry],
    ];

    if (trace.sourceReference) {
      rows.push(["Seed lot or reference", trace.sourceReference]);
    }

    if (trace.motherPlant) {
      rows.push(["Mother plant", trace.motherPlant]);
    }

    if (trace.supplier) {
      rows.push([
        "Supplier",
        trace.supplier.passportRegistrationNumber
          ? `${trace.supplier.name} (registration ${trace.supplier.passportRegistrationNumber})`
          : trace.supplier.name,
      ]);
    }

    if (trace.purchase) {
      rows.push([
        "Bought",
        trace.purchase.reference
          ? `${dayLabel(trace.purchase.purchasedOn, now)}, ${trace.purchase.reference}`
          : dayLabel(trace.purchase.purchasedOn, now),
      ]);
    }

    return rows;
  });
</script>

{#if batchId}
  <main>
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    {#if trace}
      <header class="plant-title">
        <h1>Batch {trace.traceabilityCode}</h1>
        <p>
          <strong
            >{plantName(trace.plant.commonName, trace.plant.variety)}</strong
          >
          {#if trace.plant.botanicalName}<span class="plant-botanical">
              · <em>{trace.plant.botanicalName}</em></span
            >{/if}
        </p>
      </header>

      <button
        class="link-button trace-print no-print"
        onclick={() => window.print()}
        ><Printer size={18} /> Print this report</button
      >

      <div class="split trace">
        <section class="stack">
          <h2>Where it came from</h2>
          <dl class="facts card">
            {#each origin as [name, value] (name)}
              <dt>{name}</dt>
              <dd>{value}</dd>
            {/each}
            <dt>Xylella host</dt>
            <dd>{xylella(trace.plant.xylellaHost ?? null)}</dd>
          </dl>
          {#if trace.parent}
            <p>
              Grown from batch
              <button
                class="link-button"
                onclick={() => onChoose(trace!.parent!.id)}
                >{trace.parent.traceabilityCode}</button
              >.
            </p>
          {/if}

          <h2>Plant passports received</h2>
          {#each trace.receivedPassports as passport, index (index)}
            <div class="card stack tight">
              <strong
                >{passport.supplierName}, received {dayLabel(
                  passport.receivedOn,
                  now,
                )} · {passportStatusLabels[passport.status]}</strong
              >
              <span>A: {passport.partA}</span>
              <span>B: {passport.partB}</span>
              {#if passport.partC}<span>C: {passport.partC}</span>{/if}
              <span>D: {passport.partD}</span>
              {#if passport.partE}<span>E: {passport.partE}</span>{/if}
            </div>
          {:else}
            <p class="muted">None recorded for this batch.</p>
          {/each}

          <h2>Now</h2>
          <dl class="facts card">
            <dt>Still growing</dt>
            <dd>{trace.totals.now}</dd>
            <dt>Lost</dt>
            <dd>{trace.totals.lost}</dd>
            <dt>Sold</dt>
            <dd>{trace.totals.sold}</dd>
          </dl>
          {#each stillGrowing(trace) as container (container.labelCode)}
            <button
              class="place-row"
              onclick={() => onOpenTray(container.labelCode)}
            >
              <span class="plant-names">
                <strong>{container.now} in {container.labelCode}</strong>
                <em>{container.place ?? "No place recorded"}</em>
              </span>
            </button>
          {/each}
        </section>

        <section class="stack">
          <h2>Where it went</h2>
          {#each trace.sales as sale (sale.invoiceId)}
            <div class="card stack tight">
              <strong>{sale.quantity} to {sale.customerName}</strong>
              <span class="muted"
                >Invoice {sale.number}, {dayLabel(
                  sale.issuedOn,
                  now,
                )}{sale.cancelled ? " · cancelled by a credit note" : ""}</span
              >
            </div>
          {:else}
            <p class="muted">No invoices have taken plants from this batch.</p>
          {/each}
          {#if trace.children.length > 0}
            <p>
              Split into batch{trace.children.length > 1 ? "es" : ""}
              {#each trace.children as child, index (child.id)}{index > 0
                  ? ", "
                  : " "}<button
                  class="link-button"
                  onclick={() => onChoose(child.id)}
                  >{child.traceabilityCode}</button
                >{/each}.
            </p>
          {/if}

          <h2>What was done to it</h2>
          <ol class="timeline">
            {#each trace.steps as step (step.eventId)}
              <li>
                <span class="timeline-what">{describeStep(step)}</span>
                <span class="muted credit">
                  {momentLabel(step.occurredAt, now)}{step.recordedBy
                    ? `, ${step.recordedBy}`
                    : ""}{containerOf(step)
                    ? ` · ${containerOf(step)}`
                    : ""}{step.corrected ? " · corrected" : ""}
                </span>
              </li>
            {/each}
          </ol>
          <p class="muted">Work that was undone is left out.</p>
        </section>
      </div>
    {/if}
    <button class="back no-print" onclick={() => onChoose(null)}>Back</button>
  </main>
{:else}
  <main>
    <h1>Trace a batch</h1>
    <p class="muted">
      Where a batch came from, every passport received with it, what was done to
      it and who bought it: what a plant health inspector asks to see. Find it
      by its batch code, a tray or pot it has been in, or the plant's name.
    </p>
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    <form class="row trace-search" onsubmit={find}>
      <label class="grow"
        >Batch, tray or plant <input
          bind:value={query}
          autocomplete="off"
          autocapitalize="characters"
          placeholder="B000012"
        /></label
      >
      <button type="submit">Find</button>
    </form>

    {#if !searched && matches.length > 0}
      <h2 class="settings-heading">Newest batches</h2>
    {/if}
    {#if loaded && matches.length === 0}
      <p class="muted">
        {searched ? "No batch matches that." : "No batches yet."}
      </p>
    {/if}
    <ul class="place-tree">
      {#each matches as match (match.batchId)}
        <li>
          <button class="place-row" onclick={() => onChoose(match.batchId)}>
            <span class="plant-names">
              <strong
                >{match.traceabilityCode} · {plantName(
                  match.commonName,
                  match.variety,
                )}</strong
              >
              <em
                >{sourceLabels[match.source]}, {dayLabel(
                  match.startedAt.slice(0, 10),
                  now,
                )}</em
              >
            </span>
          </button>
        </li>
      {/each}
    </ul>
    <button class="back" onclick={onBack}>Back</button>
  </main>
{/if}
