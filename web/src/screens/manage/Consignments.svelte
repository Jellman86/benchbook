<script lang="ts">
  import { crumbs } from "../../lib/crumbs.svelte";
  import { isOn } from "../../lib/features.svelte";
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
  import { addDays, isoDay } from "../../lib/days";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import { plantName } from "../../lib/work";
  import DayPicker from "../DayPicker.svelte";
  import PrintButton from "../PrintButton.svelte";
  import PackagePassportCard from "../work/PackagePassportCard.svelte";

  // Plants going out together: who to, how, and in which packages. Each package that needs a passport gets
  // one, covering every batch packed in it.
  let { onBack }: { onBack: () => void } = $props();

  let list = $state<Schemas["ConsignmentSummary"][]>([]);
  let customers = $state<Schemas["CustomerResponse"][]>([]);
  let stock = $state<Schemas["ProductionRow"][]>([]);
  // Plants already invoiced, gone from their trays, that can still go in a consignment.
  let invoiced = $state<Schemas["InvoicedPlants"][]>([]);
  let opened = $state<Schemas["ConsignmentDetails"] | null>(null);
  let writing = $state(false);

  crumbs(() =>
    opened
      ? [{ label: `Consignment ${opened.number}` }]
      : writing
        ? [{ label: "New consignment" }]
        : [],
  );
  let kind = $state<ConsignmentKind>("TradeSale");
  let customerId = $state("");
  let sentTo = $state("");
  let sentOn = $state(isoDay(new Date()));
  let notes = $state("");
  let packages = $state<DraftLine[][]>([]);
  // Made here, so sending again after a lost reply finds the same consignment rather than making a second.
  let draftId = $state("");
  let examined = $state<Record<string, boolean>>({});
  let activeGrowthConfirmed = $state<Record<string, boolean>>({});
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
  const invoicedKey = (row: { invoiceLineId: string }) =>
    `invoice:${row.invoiceLineId}`;
  const holds = (tray: string) =>
    tray.startsWith("invoice:")
      ? Number(invoiced.find((row) => invoicedKey(row) === tray)?.left ?? 0)
      : Number(trays.find((row) => trayKey(row) === tray)?.quantity ?? 0);
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
      // Invoiced plants are offered only while trade invoices are on.
      invoiced = isOn("Invoices")
        ? ((await api.GET("/api/consignments/invoiced")).data ?? [])
        : [];
      writing = true;
      problem = "";
      kind = "TradeSale";
      customerId = "";
      sentTo = "";
      sentOn = isoDay(new Date());
      notes = "";
      packages = [[{ tray: "", quantity: 1 }]];
      draftId = crypto.randomUUID();
    }, setProblem);

  const record = (event: SubmitEvent) => {
    event.preventDefault();
    busy = true;

    return attempt(
      async () => {
        const { data, error, response } = await api.POST("/api/consignments", {
          body: {
            id: draftId,
            kind,
            customerId: customerId || null,
            sentTo: customerId ? null : sentTo.trim(),
            sentOn,
            notes: optional(notes),
            packages: packages.map((lines) => ({
              lines: lines.map((line) => {
                const sold = invoiced.find(
                  (row) => invoicedKey(row) === line.tray,
                );

                if (sold) {
                  return {
                    containerId: sold.containerId,
                    batchId: sold.batchId,
                    quantity: line.quantity,
                    invoiceLineId: sold.invoiceLineId,
                  };
                }

                const [containerId, batchId] = line.tray.split("|");
                return {
                  containerId,
                  batchId,
                  quantity: line.quantity,
                  invoiceLineId: null,
                };
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
              activeGrowthConfirmed: activeGrowthConfirmed[packageId] ?? false,
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

  const remove = () => {
    if (
      !opened ||
      !confirm(
        `Remove consignment ${opened.number}? Do this only if it was recorded by mistake.`,
      )
    ) {
      return;
    }

    const id = opened.id;

    return attempt(async () => {
      const { error, response } = await api.DELETE("/api/consignments/{id}", {
        params: { path: { id } },
      });

      if (response.ok) {
        opened = null;
        problem = "";
        await load();
      } else {
        problem = consignmentProblem(
          response.status,
          (error as { problem?: string } | undefined)?.problem,
        );
      }
    }, setProblem);
  };

  // Plants from an invoice usually go to that customer: fill them in if nobody's chosen yet.
  function fromInvoice(tray: string) {
    const sold = invoiced.find((row) => invoicedKey(row) === tray);

    if (sold && !customerId && !sentTo.trim()) {
      customerId = sold.customerId;
    }
  }

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
          {#each pack.lines as line, index (index)}
            <li>
              <strong>{line.quantity} {line.plantName}</strong>
              <span class="muted">
                · batch {line.traceabilityCode}, from {line.labelCode}{line.invoiceNumber
                  ? `, sold on invoice ${line.invoiceNumber}`
                  : ""}</span
              >
            </li>
          {/each}
        </ul>

        {#if pack.lines.some((line) => line.propagatingMaterial)}
          <p class="caution">
            Ornamental propagating material, including seeds, plugs, cuttings
            and liners, must also travel with a separate supplier document. A
            plant passport does not replace it.
          </p>
        {/if}
        {#if pack.passport}
          <PackagePassportCard
            caption="Passport for package {pack.number}"
            partB={pack.passport.partB}
            lines={pack.passport.lines}
          >
            <span>Issued {dayLabel(pack.passport.issuedOn, now)}</span>
            {#each pack.passport.lines as line (line.partC + line.partD)}
              <span class="muted">{line.partC}, D: {line.rule}</span>
            {/each}
          </PackagePassportCard>
          <PrintButton
            href="/api/labels/passports/{pack.id}"
            label="Print the passport"
          />
        {:else if consignment.passportNeeded}
          <label class="tick"
            ><input
              type="checkbox"
              bind:checked={activeGrowthConfirmed[pack.id]}
            />
            Where origin depends on time here, I confirm active growth for at least
            two weeks under protection or four weeks outside, without a break. Leave
            unticked to keep the original country.</label
          >
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
        One passport covers each package: part B once, then each batch's
        botanical name, code and origin together. The examination is recorded on
        each tray with your name.
      </p>
    {/if}
    {#if consignment.packages.every((pack) => !pack.passport)}
      <button class="secondary danger" disabled={busy} onclick={remove}
        >Remove this consignment</button
      >
    {/if}
    <button class="back" onclick={() => (opened = null)}>Back</button>
  </main>
{:else if writing}
  <main>
    <h1>Send plants</h1>
    <p class="caution">
      If these are ornamental propagating material (including seeds, plugs,
      cuttings or liners), prepare a separate supplier document to travel with
      them. A passport does not replace it.
    </p>
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
        <DayPicker
          bind:value={sentOn}
          label="Going on"
          max={addDays(isoDay(new Date()), 14)}
        />
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
          {#if trays.length === 0 && invoiced.length === 0}
            <p class="muted">No counted trays to send from.</p>
          {/if}
          {#each lines as line, lineIndex (lineIndex)}
            <div class="consignment-line">
              <label
                >Tray <select
                  bind:value={line.tray}
                  required
                  onchange={() => fromInvoice(line.tray)}
                >
                  <option value="">Choose…</option>
                  <optgroup label="In the trays">
                    {#each trays as row (trayKey(row))}
                      <option value={trayKey(row)}>{trayWords(row)}</option>
                    {/each}
                  </optgroup>
                  {#if invoiced.length > 0}
                    <optgroup label="Already invoiced">
                      {#each invoiced as row (row.invoiceLineId)}
                        <option value={invoicedKey(row)}
                          >Invoice {row.invoiceNumber} · {row.left}
                          {row.plantName} · {row.customerName}</option
                        >
                      {/each}
                    </optgroup>
                  {/if}
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
        Plants can come from the trays, or from an invoice if they've been
        invoiced already. Recording a consignment doesn't change what's in the
        trays: an invoice or a loss does that.
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
