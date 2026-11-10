<script lang="ts">
  import FormPanel from "../FormPanel.svelte";
  import { onMount } from "svelte";
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

  const today = () => new Date().toISOString().slice(0, 10);
  const emptyLine = () => ({ description: "", quantity: "", net: "", vat: "" });

  let purchases = $state<Schemas["PurchaseResponse"][]>([]);
  let suppliers = $state<Schemas["SupplierResponse"][]>([]);
  let supplierId = $state("");
  let purchasedOn = $state(today());
  let reference = $state("");
  let lines = $state([emptyLine()]);
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

    return attempt(async () => {
      const { response } = await api.POST("/api/purchases", {
        body: {
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
          })),
        },
      });

      if (response.ok) {
        reference = "";
        lines = [emptyLine()];
        await load();
      } else {
        problem = "Please check the supplier, date and amounts.";
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

  <div class="split">
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
          <label
            >Date <input type="date" bind:value={purchasedOn} required /></label
          >
          <label>Invoice reference <input bind:value={reference} /></label>

          {#each lines as line, index (index)}
            <div class="card">
              <label
                >Item <input bind:value={line.description} required /></label
              >
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
          <button type="submit">Save purchase</button>
        </form>
      </FormPanel>
    </section>

    <section>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
      <p><strong>Still owed: {money(owed)}</strong></p>

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
              {#if Number(line.batchCount) > 0}
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
                onclick={() => setPaid(purchase.id, null)}>Not paid</button
              >
            {:else}
              <button
                class="secondary"
                onclick={() => setPaid(purchase.id, today())}>Mark paid</button
              >
            {/if}
            <button class="link-button" onclick={() => voidPurchase(purchase)}
              >Void</button
            >
          </div>
        </div>
      {:else}
        <p class="muted">No purchases recorded yet.</p>
      {/each}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
