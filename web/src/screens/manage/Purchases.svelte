<script lang="ts">
  import { isoDay } from "../../lib/days";
  import DayPicker from "../DayPicker.svelte";
  import FormPanel from "../FormPanel.svelte";
  import { onMount } from "svelte";
  import FileUp from "@lucide/svelte/icons/file-up";
  import Paperclip from "@lucide/svelte/icons/paperclip";
  import { api } from "../../api/client";
  import { dayLabel } from "../../lib/dates";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import { money, type BatchPrefill } from "../../lib/work";

  let {
    onBack,
    onStartBatch,
  }: {
    onBack: () => void;
    onStartBatch: (prefill: BatchPrefill) => void;
  } = $props();

  const today = () => isoDay(new Date());
  const emptyLine = () => ({
    description: "",
    quantity: "",
    net: "",
    vat: "",
    isPlants: true,
  });

  let purchases = $state<Schemas["PurchaseResponse"][]>([]);
  let suppliers = $state<Schemas["SupplierResponse"][]>([]);
  let supplierId = $state("");
  let purchasedOn = $state(today());
  let reference = $state("");
  let lines = $state([emptyLine()]);
  let requestId = crypto.randomUUID();
  let busy = $state(false);
  let reading = $state(false);
  let readNote = $state("");

  /** Fills the form from a supplier's PDF invoice, for a person to check before saving. */
  const readInvoice = (event: Event) => {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    return attempt(async () => {
      reading = true;

      try {
        const response = await fetch("/api/purchases/read-invoice", {
          method: "POST",
          body: file,
        });
        input.value = "";

        if (!response.ok) {
          readNote =
            "That PDF couldn't be read. Photos and scans can't be read, so please type it in.";
          return;
        }

        const draft = (await response.json()) as Schemas["InvoiceDraft"];
        reference = draft.reference ?? reference;
        purchasedOn = draft.date ?? purchasedOn;

        if (draft.lines.length > 0) {
          lines = draft.lines.map((line) => ({
            description: line.description,
            quantity: line.quantity === null ? "" : String(line.quantity),
            net: String(line.netCost),
            vat: "",
            isPlants: true,
          }));
        }

        readNote =
          draft.lines.length > 0
            ? `Filled in ${draft.lines.length} line${draft.lines.length === 1 ? "" : "s"} from the invoice. Please check each one, mark compost and pots, and add the VAT.`
            : "No item lines could be found in that PDF, so please type them in.";
      } finally {
        reading = false;
      }
    }, setProblem);
  };
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  const owed = $derived(
    purchases
      .filter((purchase) => !purchase.paidOn)
      .reduce(
        (total, purchase) =>
          total + Number(purchase.netTotal) + Number(purchase.vatTotal),
        0,
      ),
  );

  async function load() {
    purchases = (await api.GET("/api/purchases")).data ?? [];
  }

  onMount(() =>
    attempt(async () => {
      await load();
      suppliers = (await api.GET("/api/suppliers")).data ?? [];
    }, setProblem),
  );

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    if (busy) return;
    return attempt(async () => {
      busy = true;
      try {
        const { response } = await api.POST("/api/purchases", {
          body: {
            id: requestId,
            supplierId,
            purchasedOn,
            reference: optional(reference),
            notes: null,
            lines: lines.map((line) => ({
              description: line.description,
              quantity:
                line.quantity.trim() === "" ? null : Number(line.quantity),
              netCost: Number(line.net),
              vat: Number(line.vat || 0),
              isPlants: line.isPlants,
            })),
          },
        });

        if (response.ok) {
          requestId = crypto.randomUUID();
          reference = "";
          lines = [emptyLine()];
          await load();
        } else {
          problem = "Please check the supplier, date and amounts.";
        }
      } finally {
        busy = false;
      }
    }, setProblem);
  };

  const setPaid = (id: string, paidOn: string | null) =>
    attempt(async () => {
      await api.POST("/api/purchases/{id}/paid", {
        params: { path: { id } },
        body: { paidOn },
      });
      await load();
    }, setProblem);

  const addReceipt = (purchaseId: string, event: Event) => {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    return attempt(async () => {
      const response = await fetch(`/api/purchases/${purchaseId}/receipts`, {
        method: "POST",
        body: file,
      });

      input.value = "";

      if (response.ok) {
        await load();
      } else {
        problem =
          response.status === 413
            ? "That file is over 10MB."
            : "Please choose a photo (JPEG or PNG) or a PDF.";
      }
    }, setProblem);
  };

  const voidPurchase = (purchase: Schemas["PurchaseResponse"]) => {
    if (!confirm(`Void this purchase from ${purchase.supplierName}?`)) {
      return;
    }

    return attempt(async () => {
      await api.POST("/api/purchases/{id}/void", {
        params: { path: { id: purchase.id } },
      });
      await load();
    }, setProblem);
  };
</script>

<main>
  <h1>Purchases</h1>

  <div class="split wide-list">
    <section>
      <FormPanel label="Add a purchase">
        <form class="stack" onsubmit={save}>
          <label>
            Supplier
            <select bind:value={supplierId} required>
              <option value="">Choose…</option>
              {#each suppliers as supplier (supplier.id)}
                <option value={supplier.id}>{supplier.name}</option>
              {/each}
            </select>
          </label>
          <DayPicker bind:value={purchasedOn} label="Date" />
          <label class="photo-choice">
            <FileUp size={20} />
            <span
              >{reading
                ? "Reading the invoice…"
                : "Fill in from the supplier's PDF invoice"}</span
            >
            <input
              type="file"
              accept="application/pdf"
              onchange={readInvoice}
              disabled={reading}
            />
          </label>
          {#if readNote}<p class="muted" role="status">{readNote}</p>{/if}
          <label>Invoice reference <input bind:value={reference} /></label>

          {#each lines as line, index (index)}
            <div class="card">
              <label
                >Item <input bind:value={line.description} required /></label
              >
              <div class="segmented two" role="group" aria-label="What it is">
                <button
                  type="button"
                  aria-pressed={line.isPlants}
                  onclick={() => (line.isPlants = true)}>Plants</button
                >
                <button
                  type="button"
                  aria-pressed={!line.isPlants}
                  onclick={() => (line.isPlants = false)}
                  >Compost, pots and other things</button
                >
              </div>
              <label
                >Quantity <input
                  bind:value={line.quantity}
                  inputmode="numeric"
                /></label
              >
              <label
                >Cost before VAT (£) <input
                  bind:value={line.net}
                  inputmode="decimal"
                  required
                /></label
              >
              <label
                >VAT (£) <input
                  bind:value={line.vat}
                  inputmode="decimal"
                /></label
              >
            </div>
          {/each}

          <button
            type="button"
            class="secondary"
            onclick={() => lines.push(emptyLine())}>Add another item</button
          >
          <button type="submit" disabled={busy}>Save purchase</button>
        </form>
      </FormPanel>
    </section>

    <section>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
      <p><strong>Still owed: {money(owed)}</strong></p>

      <div class="mobile-only stack">
        {#each purchases as purchase (purchase.id)}
          <div class="card">
            <strong
              >{purchase.supplierName}: {money(
                Number(purchase.netTotal) + Number(purchase.vatTotal),
              )}</strong
            >
            <span
              >{dayLabel(purchase.purchasedOn)}{purchase.reference
                ? `, ${purchase.reference}`
                : ""}</span
            >
            <span
              >{purchase.paidOn
                ? `Paid ${dayLabel(purchase.paidOn)}`
                : "Not paid"}</span
            >
            {#each purchase.lines as line (line.id)}
              <div class="line">
                <span class="muted"
                  >{line.description}: {money(Number(line.netCost))} + VAT {money(
                    Number(line.vat),
                  )}</span
                >
                {#if line.isPlants === false}
                  <!-- Sundries such as compost never become batches. -->
                {:else if Number(line.batchCount) > 0}
                  <span class="muted">Batch started</span>
                {:else}
                  <button
                    class="secondary"
                    onclick={() =>
                      onStartBatch({
                        supplierId: purchase.supplierId,
                        quantity: Number(line.quantity ?? 1),
                        purchaseLineId: line.id,
                        description: line.description,
                        receivedOn: purchase.purchasedOn,
                      })}>Start a batch</button
                  >
                {/if}
              </div>
            {/each}
            {#each purchase.receiptIds as receipt, index (receipt)}
              <a
                href="/api/purchases/receipts/{receipt}"
                target="_blank"
                rel="noopener">Receipt {index + 1}</a
              >
            {/each}
            <label class="photo-choice">
              <Paperclip size={20} />
              <span>Add a receipt (photo or PDF)</span>
              <input
                type="file"
                accept="image/jpeg,image/png,application/pdf"
                onchange={(event) => addReceipt(purchase.id, event)}
              />
            </label>
            <div class="row">
              {#if purchase.paidOn}
                <button
                  class="secondary"
                  onclick={() => setPaid(purchase.id, null)}
                  >Mark as not paid</button
                >
              {:else}
                <button
                  class="secondary"
                  onclick={() => setPaid(purchase.id, today())}
                  >Mark paid</button
                >
              {/if}
              <button class="link-button" onclick={() => voidPurchase(purchase)}
                >Void</button
              >
            </div>
          </div>
        {:else}
          <p class="note-empty muted">No purchases recorded yet.</p>
        {/each}
      </div>
      <div class="desktop-only">
        {#if purchases.length > 0}
          <div class="table-wrap">
            <table class="data-table purchases-table">
              <caption class="visually-hidden">Purchases</caption>
              <thead>
                <tr>
                  <th scope="col">Day</th>
                  <th scope="col">Supplier</th>
                  <th scope="col">What</th>
                  <th scope="col" class="numeric">Total</th>
                  <th scope="col">Paid</th>
                  <th scope="col"
                    ><span class="visually-hidden">Actions</span></th
                  >
                </tr>
              </thead>
              <tbody>
                {#each purchases as purchase (purchase.id)}
                  <tr>
                    <td class="nowrap">{dayLabel(purchase.purchasedOn)}</td>
                    <td>
                      {purchase.supplierName}
                      {#if purchase.reference}<span class="muted cell-note"
                          >{purchase.reference}</span
                        >{/if}
                    </td>
                    <td>
                      <ul class="cell-lines">
                        {#each purchase.lines as line (line.id)}
                          <li>
                            <span
                              >{line.description}
                              <span class="muted"
                                >{money(Number(line.netCost))} + VAT {money(
                                  Number(line.vat),
                                )}</span
                              ></span
                            >
                            {#if line.isPlants === false}
                              <!-- Sundries such as compost never become batches. -->
                            {:else if Number(line.batchCount) > 0}
                              <span class="muted">Batch started</span>
                            {:else}
                              <button
                                class="link-button"
                                onclick={() =>
                                  onStartBatch({
                                    supplierId: purchase.supplierId,
                                    quantity: Number(line.quantity ?? 1),
                                    purchaseLineId: line.id,
                                    description: line.description,
                                    receivedOn: purchase.purchasedOn,
                                  })}>Start a batch</button
                              >
                            {/if}
                          </li>
                        {/each}
                      </ul>
                    </td>
                    <td class="numeric"
                      >{money(
                        Number(purchase.netTotal) + Number(purchase.vatTotal),
                      )}</td
                    >
                    <td class="nowrap"
                      >{purchase.paidOn
                        ? dayLabel(purchase.paidOn)
                        : "Not paid"}</td
                    >
                    <td>
                      <div class="cell-actions">
                        {#if purchase.paidOn}
                          <button
                            class="link-button"
                            onclick={() => setPaid(purchase.id, null)}
                            >Not paid</button
                          >
                        {:else}
                          <button
                            class="link-button"
                            onclick={() => setPaid(purchase.id, today())}
                            >Mark paid</button
                          >
                        {/if}
                        {#each purchase.receiptIds as receipt, index (receipt)}
                          <a
                            href="/api/purchases/receipts/{receipt}"
                            target="_blank"
                            rel="noopener">Receipt {index + 1}</a
                          >
                        {/each}
                        <label class="file-link">
                          Add receipt
                          <input
                            type="file"
                            accept="image/jpeg,image/png,application/pdf"
                            onchange={(event) => addReceipt(purchase.id, event)}
                          />
                        </label>
                        <button
                          class="link-button danger-link"
                          onclick={() => voidPurchase(purchase)}>Void</button
                        >
                      </div>
                    </td>
                  </tr>
                {/each}
              </tbody>
            </table>
          </div>
        {:else}
          <p class="note-empty muted">No purchases recorded yet.</p>
        {/if}
      </div>
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
