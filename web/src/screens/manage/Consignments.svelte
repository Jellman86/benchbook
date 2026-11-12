<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import {
    consignmentProblem,
    kindChoices,
    kindLabel,
    overdrawn,
    packagesReady,
    passportsLeft,
    type ConsignmentKind,
    type DraftLine,
  } from "../../lib/consignments";
  import { dayLabel } from "../../lib/dates";
  import { isoDay } from "../../lib/days";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import { plantName } from "../../lib/work";
  import DayPicker from "../DayPicker.svelte";
  import PrintButton from "../PrintButton.svelte";
  import PassportCard from "../work/PassportCard.svelte";

  // Plants going out together: who to, how, and in which packages. Each package that needs a passport gets
  // one, covering every batch packed in it.
  let { onBack }: { onBack: () => void } = $props();

  let list = $state<Schemas["ConsignmentSummary"][]>([]);
  let customers = $state<Schemas["CustomerResponse"][]>([]);
  let stock = $state<Schemas["ProductionRow"][]>([]);
  let opened = $state<Schemas["ConsignmentDetails"] | null>(null);
  let writing = $state(false);
  let kind = $state<ConsignmentKind>("TradeSale");
  let customerId = $state("");
  let sentTo = $state("");
  let sentOn = $state(isoDay(new Date()));
  let notes = $state("");
  let packages = $state<DraftLine[][]>([]);
  let examined = $state<Record<string, boolean>>({});
  let findings = $state<Record<string, string>>({});
  let busy = $state(false);
  let problem = $state("");

  const now = new Date();
  const setProblem = (message: string) => (problem = message);
  const trayKey = (row: { containerId: string; batchId: string }) =>
    `${row.containerId}|${row.batchId}`;

  // Only counted trays have plants to send.
  const trays = $derived(
    stock.filter((row) => !row.notCounted && Number(row.quantity) > 0),
  );
  const holds = (tray: string) =>
    Number(trays.find((row) => trayKey(row) === tray)?.quantity ?? 0);
  const choice = $derived(kindChoices.find((item) => item.kind === kind)!);
  const tooMany = $derived(overdrawn(packages, holds));
  const ready = $derived(
    packagesReady(packages) &&
      tooMany.length === 0 &&
      (customerId !== "" || sentTo.trim() !== ""),
  );

  async function load() {
    list = (await api.GET("/api/consignments")).data ?? [];
  }

  onMount(() =>
    attempt(async () => {
      await load();
      customers = (await api.GET("/api/customers")).data ?? [];
    }, setProblem),
  );

  const open = (id: string) =>
    attempt(async () => {
      opened =
        (
          await api.GET("/api/consignments/{id}", {
            params: { path: { id } },
          })
        ).data ?? null;
    }, setProblem);

  const start = () =>
    attempt(async () => {
      stock = (await api.GET("/api/production")).data ?? [];
      writing = true;
      problem = "";
      kind = "TradeSale";
      customerId = "";
      sentTo = "";
      sentOn = isoDay(new Date());
      notes = "";
      packages = [[{ tray: "", quantity: 1 }]];
    }, setProblem);

  const record = (event: SubmitEvent) => {
    event.preventDefault();
    busy = true;

    return attempt(
      async () => {
        const { data, error, response } = await api.POST("/api/consignments", {
          body: {
            kind,
            customerId: customerId || null,
            sentTo: customerId ? null : sentTo.trim(),
            sentOn,
            notes: optional(notes),
            packages: packages.map((lines) => ({
              lines: lines.map((line) => {
                const [containerId, batchId] = line.tray.split("|");
                return { containerId, batchId, quantity: line.quantity };
              }),
            })),
          },
        });
        busy = false;

        if (data) {
          writing = false;
          problem = "";
          await load();
          await open(data.id);
        } else {
          problem = consignmentProblem(
            response.status,
            (error as { problem?: string } | undefined)?.problem,
          );
        }
      },
      (message) => {
        busy = false;
        setProblem(message);
      },
    );
  };

  const issue = (packageId: string) => {
    if (!opened) {
      return;
    }

    const id = opened.id;
    busy = true;

    return attempt(
      async () => {
        const { error, response } = await api.POST(
          "/api/consignments/{id}/packages/{packageId}/passport",
          {
            params: { path: { id, packageId } },
            body: {
              examined: examined[packageId] ?? false,
              findings: optional(findings[packageId] ?? ""),
            },
          },
        );
        busy = false;
        problem = response.ok
          ? ""
          : consignmentProblem(
              response.status,
              (error as { problem?: string } | undefined)?.problem,
            );
        await open(id);
        await load();
      },
      (message) => {
        busy = false;
        setProblem(message);
      },
    );
  };

  const trayWords = (row: Schemas["ProductionRow"]) =>
    `${row.labelCode} · ${plantName(row.commonName, row.variety)} · ${row.quantity} there${row.locationName ? ` · ${row.locationName}` : ""}`;

  const plural = (n: number | string, word: string) =>
    `${n} ${word}${Number(n) === 1 ? "" : "s"}`;
</script>

{#if opened}
  {@const consignment = opened}
  <main>
    <h1>Consignment {consignment.number}</h1>
    <p>
      <strong>{kindLabel(consignment.kind)}</strong> to {consignment.sentTo},
      {dayLabel(consignment.sentOn, now)}
    </p>
    <p class="card {consignment.passportNeeded ? '' : 'muted'}">
      {consignment.why}
    </p>
    {#if consignment.notes}<p class="muted">{consignment.notes}</p>{/if}
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

    {#each consignment.packages as pack (pack.id)}
      <section class="form-card stack" aria-label="Package {pack.number}">
        <h2 class="form-heading">Package {pack.number}</h2>
        <ul class="plain-list">
          {#each pack.lines as line (line.batchId + line.labelCode)}
            <li>
              <strong>{line.quantity} {line.plantName}</strong>
              <span class="muted">
                · batch {line.traceabilityCode}, from {line.labelCode}</span
              >
            </li>
          {/each}
        </ul>

        {#if pack.passport}
          <PassportCard
            caption="Passport for package {pack.number}"
            partA={pack.passport.partA}
            partB={pack.passport.partB}
            partC={pack.passport.partC}
            partD={pack.passport.partD}
          >
            <span>Issued {dayLabel(pack.passport.issuedOn, now)}</span>
            {#each pack.passport.rules as rule (rule)}
              <span class="muted">D for {rule}</span>
            {/each}
          </PassportCard>
          <PrintButton
            href="/api/labels/passports/{pack.id}"
            label="Print the passport"
          />
        {:else if consignment.passportNeeded}
          <label class="tick">
            <input type="checkbox" bind:checked={examined[pack.id]} />
            I've examined the plants in this package just now and they're fit to move
          </label>
          <label
            >Anything noticed (optional) <input
              bind:value={findings[pack.id]}
              maxlength="200"
              enterkeyhint="done"
              placeholder="No pests or diseases seen"
            /></label
          >
          <button
            disabled={busy || !examined[pack.id]}
            onclick={() => issue(pack.id)}>Issue its passport</button
          >
        {/if}
      </section>
    {/each}
    {#if consignment.passportNeeded}
      <p class="muted">
        One passport covers each package: every plant in part A and every
        batch's code in part C. The examination is recorded on each tray with
        your name.
      </p>
    {/if}
    <button class="back" onclick={() => (opened = null)}>Back</button>
  </main>
{:else if writing}
  <main>
    <h1>Send plants</h1>
    <form class="stack" onsubmit={record}>
      <fieldset class="form-card stack">
        <legend class="field-label">How are they going?</legend>
        <div class="segmented two">
          {#each kindChoices as item (item.kind)}
            <button
              type="button"
              class={kind === item.kind ? "" : "secondary"}
              aria-pressed={kind === item.kind}
              onclick={() => (kind = item.kind)}>{item.label}</button
            >
          {/each}
        </div>
        <p class={choice.needed ? "card" : "muted"} role="status">
          <strong>{choice.needed ? "Passport needed." : "No passport."}</strong>
          {choice.why}
        </p>
      </fieldset>

      <div class="form-card stack">
        <label
          >Going to
          <select bind:value={customerId}>
            <option value="">Someone not on the customer list</option>
            {#each customers as customer (customer.id)}
              <option value={customer.id}>{customer.name}</option>
            {/each}
          </select></label
        >
        {#if !customerId}
          <label
            >Name <input
              bind:value={sentTo}
              maxlength="200"
              autocapitalize="words"
              placeholder="Who the plants go to"
            /></label
          >
        {/if}
        <DayPicker bind:value={sentOn} label="Sent on" max={null} />
        <label
          >Notes (optional) <input
            bind:value={notes}
            maxlength="200"
            placeholder="Courier, order number…"
          /></label
        >
      </div>

      {#each packages as lines, index (index)}
        <fieldset class="form-card stack">
          <legend>Package {index + 1}</legend>
          {#if trays.length === 0}
            <p class="muted">No counted trays to send from.</p>
          {/if}
          {#each lines as line, lineIndex (lineIndex)}
            <div class="consignment-line">
              <label
                >Tray <select bind:value={line.tray} required>
                  <option value="">Choose…</option>
                  {#each trays as row (trayKey(row))}
                    <option value={trayKey(row)}>{trayWords(row)}</option>
                  {/each}
                </select></label
              >
              <label
                >Plants <input
                  type="number"
                  inputmode="numeric"
                  min="1"
                  bind:value={line.quantity}
                  required
                /></label
              >
              {#if lines.length > 1}
                <button
                  type="button"
                  class="link-button"
                  onclick={() => lines.splice(lineIndex, 1)}>Remove</button
                >
              {/if}
            </div>
            {#if tooMany.includes(line.tray)}
              <p class="problem">
                That tray holds {holds(line.tray)}, fewer than asked for across
                the packages.
              </p>
            {/if}
          {/each}
          <div class="row">
            <button
              type="button"
              class="secondary"
              onclick={() => lines.push({ tray: "", quantity: 1 })}
              >Another tray</button
            >
            {#if packages.length > 1}
              <button
                type="button"
                class="link-button"
                onclick={() => packages.splice(index, 1)}
                >Remove the package</button
              >
            {/if}
          </div>
        </fieldset>
      {/each}
      <button
        type="button"
        class="secondary"
        onclick={() => packages.push([{ tray: "", quantity: 1 }])}
        >Another package</button
      >
      <p class="muted">
        Recording a consignment doesn't take plants out of the trays: an invoice
        or a loss does that.
      </p>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
      <div class="row">
        <button type="submit" disabled={busy || !ready}>Record it</button>
        <button
          type="button"
          class="secondary"
          onclick={() => (writing = false)}>Cancel</button
        >
      </div>
    </form>
  </main>
{:else}
  <main>
    <h1>Consignments</h1>
    <p class="muted">
      Plants going out together, packed in one or more packages. A package going
      to a business, or by post or courier, gets one plant passport covering
      everything in it.
    </p>
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    <button onclick={start}>Send plants</button>
    <ul class="place-tree">
      {#each list as item (item.id)}
        <li>
          <button class="place-row" onclick={() => open(item.id)}>
            <span class="plant-names">
              <strong>{item.number} · {item.sentTo}</strong>
              <em
                >{kindLabel(item.kind)}, {dayLabel(item.sentOn, now)} · {plural(
                  item.packages,
                  "package",
                )}{item.passportNeeded
                  ? passportsLeft(item) > 0
                    ? ` · ${plural(passportsLeft(item), "passport")} to issue`
                    : " · passports issued"
                  : ""}</em
              >
            </span>
          </button>
        </li>
      {:else}
        <li class="muted">None yet.</li>
      {/each}
    </ul>
    <button class="back" onclick={onBack}>Back</button>
  </main>
{/if}
