<script lang="ts">
  import FileDown from "@lucide/svelte/icons/file-down";
  import Coins from "@lucide/svelte/icons/coins";
  import Trash2 from "@lucide/svelte/icons/trash-2";
  import { crumbs } from "../../lib/crumbs.svelte";
  import { isoDay } from "../../lib/days";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { dayLabel } from "../../lib/dates";
  import {
    invoiceNumber,
    catalogueNet,
    changeRate,
    standing,
    totals,
    vatRates,
    type DraftLine,
    type InvoiceSummary,
  } from "../../lib/invoices";
  import { byContainer } from "../../lib/places";
  import type { Column } from "../../lib/table";
  import { attempt, type Schemas } from "../../lib/types";
  import { money, plantName } from "../../lib/work";
  import DataTable from "../DataTable.svelte";

  let { onBack }: { onBack: () => void } = $props();

  let invoices = $state<InvoiceSummary[]>([]);
  let customers = $state<Schemas["CustomerResponse"][]>([]);
  let plants = $state<Schemas["PlantResponse"][]>([]);
  let potSizes = $state<Schemas["PotSizeResponse"][]>([]);
  let stock = $state<Schemas["ProductionRow"][]>([]);
  let vatRegistered = $state(false);
  let writing = $state(false);

  crumbs(() => (writing ? [{ label: "New invoice" }] : []));
  let customerId = $state("");
  let notes = $state("");
  let lines = $state<DraftLine[]>([]);
  let problem = $state("");
  let issued = $state<{ id: string; number: number } | null>(null);
  let requestId = crypto.randomUUID();
  let busy = $state(false);

  const today = isoDay(new Date());

  // The form can open before the customers have loaded; choose the first once they have.
  $effect(() => {
    if (writing && !customerId && customers.length > 0) {
      customerId = customers[0].id;
    }
  });
  const sums = $derived(totals(lines));
  const setProblem = (message: string) => (problem = message);

  const columns: Column<InvoiceSummary>[] = [
    {
      label: "No.",
      value: (row) => Number(row.number),
      text: (row) => invoiceNumber(row.number),
      numeric: true,
    },
    {
      label: "Date",
      value: (row) => row.issuedOn,
      text: (row) => dayLabel(row.issuedOn),
    },
    { label: "Customer", value: (row) => row.customerName },
    {
      label: "Total",
      value: (row) => Number(row.total),
      text: (row) => money(Number(row.total)),
      numeric: true,
    },
    { label: "Standing", value: (row) => standing(row, today) },
  ];

  async function load() {
    invoices = (await api.GET("/api/invoices")).data ?? [];
    customers = (await api.GET("/api/customers")).data ?? [];
    plants = (await api.GET("/api/plants")).data ?? [];
    potSizes = (await api.GET("/api/pot-sizes")).data ?? [];
    stock = (await api.GET("/api/production")).data ?? [];
    vatRegistered =
      (await api.GET("/api/settings/nursery")).data?.vatRegistered ?? false;
  }

  onMount(() => void attempt(load, setProblem));

  const emptyLine = (): DraftLine => ({
    description: "",
    quantity: 1,
    unitPrice: 0,
    vatRate: vatRegistered ? 20 : 0,
    plantId: null,
    containerId: null,
    batchId: null,
  });

  function start() {
    requestId = crypto.randomUUID();
    writing = true;
    issued = null;
    problem = "";
    customerId = customers[0]?.id ?? "";
    notes = "";
    lines = [emptyLine()];
  }

  /** Choosing a plant and pot size fills in the description and price from the catalogue. */
  function choosePlant(index: number, value: string) {
    const [plantId, potSizeId] = value.split("|");
    const plant = plants.find((item) => item.id === plantId);

    if (!plant) {
      lines[index] = {
        ...lines[index],
        plantId: null,
        containerId: null,
        batchId: null,
      };
      return;
    }

    const size = potSizes.find((item) => item.id === potSizeId);
    const price = plant.prices.find((item) => item.potSizeId === potSizeId);
    lines[index] = {
      ...lines[index],
      plantId: plant.id,
      description: `${plantName(plant.commonName, plant.variety)}${size ? ` ${size.name}` : ""}`,
      unitPrice: price
        ? catalogueNet(Number(price.price), lines[index].vatRate, vatRegistered)
        : lines[index].unitPrice,
      catalogueGross: price ? Number(price.price) : null,
      containerId: null,
      batchId: null,
    };
  }

  const plantChoices = $derived(
    plants.flatMap((plant) =>
      plant.prices.length > 0
        ? plant.prices.map((price) => ({
            value: `${plant.id}|${price.potSizeId}`,
            label: `${plantName(plant.commonName, plant.variety)}, ${potSizes.find((size) => size.id === price.potSizeId)?.name ?? ""}`,
          }))
        : [
            {
              value: `${plant.id}|`,
              label: plantName(plant.commonName, plant.variety),
            },
          ],
    ),
  );

  const traysOf = (plantId: string | null) =>
    plantId
      ? byContainer(
          // A tray nobody has counted has nothing to take from yet.
          stock.filter((row) => row.plantId === plantId && !row.notCounted),
        ).map((rows) => rows[0])
      : [];

  function chooseTray(index: number, value: string) {
    const row = stock.find(
      (item) => `${item.containerId}|${item.batchId}` === value,
    );
    lines[index] = {
      ...lines[index],
      containerId: row?.containerId ?? null,
      batchId: row?.batchId ?? null,
    };
  }

  const issue = (event: SubmitEvent) => {
    event.preventDefault();

    if (
      !confirm(
        "Issue this invoice? Once issued it can't be changed, only cancelled with a credit note.",
      )
    ) {
      return;
    }

    return attempt(async () => {
      busy = true;

      try {
        const { data, response } = await api.POST("/api/invoices", {
          body: {
            id: requestId,
            customerId,
            issuedOn: null,
            notes: notes || null,
            lines: lines.map((line) => {
              const sent = { ...line };
              delete sent.catalogueGross;
              return sent;
            }),
          },
        });

        if (response.ok && data) {
          issued = { id: data.id, number: Number(data.number) };
          writing = false;
          problem = "";
          await load();
        } else {
          problem =
            response.status === 409
              ? "One of the trays doesn't have that many plants. Check the numbers."
              : "Please check every line has a description, a quantity and a price.";
        }
      } finally {
        busy = false;
      }
    }, setProblem);
  };

  const setPaid = (invoice: InvoiceSummary, paid: boolean) =>
    attempt(async () => {
      await api.POST("/api/invoices/{id}/paid", {
        params: { path: { id: invoice.id } },
        body: { paidOn: paid ? today : null },
      });
      await load();
    }, setProblem);

  const credit = (invoice: InvoiceSummary) => {
    if (
      !confirm(
        `Cancel invoice ${invoiceNumber(invoice.number)} with a credit note? Any plants it took go back in their trays.`,
      )
    ) {
      return;
    }

    return attempt(async () => {
      const { response, error } = await api.POST("/api/invoices/{id}/credit", {
        params: { path: { id: invoice.id } },
      });
      const why = (error as { problem?: string } | undefined)?.problem;
      problem = response.ok
        ? ""
        : why === "AlreadyCredited"
          ? "That invoice already has a credit note."
          : why === "PlantsOnSite"
            ? "Its plants would go back to a tray on a site, and trays can't stand on a site. Move that tray to a greenhouse, bench or bed first."
            : "The plants couldn't go back in their trays, so no credit note was made.";
      await load();
    }, setProblem);
  };
</script>

{#snippet rowActions(invoice: InvoiceSummary)}
  <a
    class="small-button"
    href="/api/invoices/{invoice.id}/pdf"
    target="_blank"
    rel="noopener"><FileDown size={16} aria-hidden="true" /> PDF</a
  >
  {#if invoice.kind === "Invoice" && !invoice.credited}
    {#if invoice.paidOn}
      <button class="link-button" onclick={() => setPaid(invoice, false)}
        ><Coins size={16} aria-hidden="true" />Mark as not paid</button
      >
    {:else}
      <button class="link-button" onclick={() => setPaid(invoice, true)}
        ><Coins size={16} aria-hidden="true" />Mark paid</button
      >
    {/if}
    <button class="link-button" onclick={() => credit(invoice)}>Cancel</button>
  {/if}
{/snippet}

<main class:page-form={writing} class:invoice-page={writing}>
  <h1>{writing ? "New invoice" : "Invoices"}</h1>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if issued}
    <div class="card done" role="status">
      <strong class="done-heading"
        >Invoice {invoiceNumber(issued.number)} issued</strong
      >
      <a
        class="button"
        href="/api/invoices/{issued.id}/pdf"
        target="_blank"
        rel="noopener">Open the PDF</a
      >
    </div>
  {/if}

  {#if writing}
    <form class="stack" onsubmit={issue}>
      <div class="form-card stack">
        <label>
          Customer
          <select bind:value={customerId} required>
            {#each customers as customer (customer.id)}
              <option value={customer.id}>{customer.name}</option>
            {/each}
          </select>
        </label>
        {#if customers.length === 0}
          <p class="muted">Add the customer under Customers first.</p>
        {/if}
      </div>

      {#each lines as line, index (index)}
        <fieldset class="form-card invoice-line">
          <legend>Line {index + 1}</legend>
          <div class="stack line-fields">
            <div class="field-grid">
              <label class="line-plant">
                From the plant list (optional)
                <select
                  onchange={(event) =>
                    choosePlant(index, event.currentTarget.value)}
                >
                  <option value="">Something else</option>
                  {#each plantChoices as choice (choice.value)}
                    <option value={choice.value}>{choice.label}</option>
                  {/each}
                </select>
              </label>
              {#if traysOf(line.plantId).length > 0}
                <label class="line-tray">
                  Take from a tray (optional)
                  <select
                    onchange={(event) =>
                      chooseTray(index, event.currentTarget.value)}
                  >
                    <option value="">Not from a tray</option>
                    {#each traysOf(line.plantId) as tray (tray.containerId)}
                      <option value="{tray.containerId}|{tray.batchId}"
                        >{tray.labelCode} · {tray.quantity} there{tray.locationName
                          ? ` · ${tray.locationName}`
                          : ""}</option
                      >
                    {/each}
                  </select>
                </label>
              {/if}
            </div>
            <label class="line-description"
              >Description <input
                bind:value={line.description}
                required
              /></label
            >
            <div class="field-grid">
              <label class="line-quantity"
                >Quantity <input
                  type="number"
                  min="1"
                  bind:value={line.quantity}
                  required
                /></label
              >
              <label class="line-price"
                >Price each (£) <input
                  type="number"
                  min="0"
                  step="0.01"
                  bind:value={line.unitPrice}
                  oninput={() => (line.catalogueGross = null)}
                  required
                /></label
              >
              {#if vatRegistered}
                <label class="line-vat">
                  VAT
                  <select
                    value={line.vatRate}
                    onchange={(event) =>
                      (lines[index] = changeRate(
                        line,
                        Number(event.currentTarget.value),
                        vatRegistered,
                      ))}
                  >
                    {#each vatRates as rate (rate)}<option value={rate}
                        >{rate}%</option
                      >{/each}
                  </select>
                </label>
              {/if}
            </div>
            <p class="muted line-total">
              <span>{money(line.quantity * line.unitPrice)}</span>
              {#if lines.length > 1}
                <button
                  type="button"
                  class="link-button"
                  onclick={() =>
                    (lines = lines.filter((_, at) => at !== index))}
                  ><Trash2 size={16} aria-hidden="true" />Remove this line</button
                >
              {/if}
            </p>
          </div>
        </fieldset>
      {/each}

      <button
        type="button"
        class="secondary"
        onclick={() => (lines = [...lines, emptyLine()])}
        >Add another line</button
      >
      <label
        >Notes on the invoice (optional) <textarea bind:value={notes} rows="2"
        ></textarea></label
      >

      <div class="card">
        {#if vatRegistered}
          <p>Net {money(sums.net)}</p>
          {#each sums.vat as rate (rate.rate)}<p>
              VAT at {rate.rate}%: {money(rate.vat)}
            </p>{/each}
        {:else}
          <p class="muted">
            No VAT: the nursery isn't registered for VAT (change this under
            Settings, Nursery details).
          </p>
        {/if}
        <strong>Total {money(sums.total)}</strong>
      </div>

      <div class="form-actions">
        <button type="submit" disabled={busy || !customerId}
          >Issue invoice</button
        >
        <button
          type="button"
          class="secondary"
          onclick={() => (writing = false)}>Cancel</button
        >
      </div>
    </form>
  {:else}
    <button onclick={start}>New invoice</button>

    {#if invoices.length > 0}
      <div class="desktop-only">
        <DataTable
          rows={invoices}
          {columns}
          key={(row) => row.id}
          caption="Invoices and credit notes"
          actions={rowActions}
        />
      </div>
      <div class="mobile-only">
        <div class="card-grid">
          {#each invoices as invoice (invoice.id)}
            <div class="card">
              <strong
                >{invoice.kind === "CreditNote" ? "Credit note" : "Invoice"}
                {invoiceNumber(invoice.number)}: {money(
                  Number(invoice.total),
                )}</strong
              >
              <span class="muted"
                >{invoice.customerName} · {dayLabel(invoice.issuedOn)} · {standing(
                  invoice,
                  today,
                )}</span
              >
              <div class="row">{@render rowActions(invoice)}</div>
            </div>
          {/each}
        </div>
      </div>
    {:else}
      <p class="note-empty muted">No invoices yet.</p>
    {/if}
  {/if}

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
