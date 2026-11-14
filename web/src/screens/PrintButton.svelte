<script lang="ts">
  import FileDown from "@lucide/svelte/icons/file-down";
  import Printer from "@lucide/svelte/icons/printer";
  import { onMount } from "svelte";
  import { api } from "../api/client";
  import { withCopies } from "../lib/labels";
  import {
    labelOf,
    printers,
    printProblems,
    type PrinterDetails,
  } from "../lib/printing";

  // Prints a label, or several copies: straight to the nursery's printer for that label if it has one, or as a PDF.
  let {
    href,
    label,
    onPrinted,
  }: {
    href: string;
    label: string;
    // Called once a label has gone to a printer, so the home screen can put the scan box back in focus.
    onPrinted?: () => void;
  } = $props();

  let copies = $state(1);
  let choices = $state<PrinterDetails[]>([]);
  let chosenId = $state("");
  let sending = $state(false);
  let outcome = $state<{ ok: boolean; message: string } | null>(null);

  const what = $derived(labelOf(href));
  const chosen = $derived(choices.find((printer) => printer.id === chosenId));

  onMount(() => {
    void printers().then((all) => {
      choices = all.filter(
        (printer) => what && printer.labels.includes(what.kind),
      );
      chosenId = choices[0]?.id ?? "";
    });
  });

  async function print() {
    if (!chosen || !what) {
      return;
    }

    sending = true;
    outcome = null;

    try {
      const { response, error } = await api.POST("/api/printers/{id}/print", {
        params: { path: { id: chosen.id } },
        body: {
          kind: what.kind,
          target: what.target,
          potSizeId: what.potSizeId,
          copies,
        },
      });

      outcome = response.ok
        ? {
            ok: true,
            message: `Sent to ${chosen.name}${copies > 1 ? `, ${copies} copies` : ""}.`,
          }
        : {
            ok: false,
            message:
              printProblems[
                (error as { problem?: string } | undefined)?.problem ?? ""
              ] ?? "That didn't print. Try the PDF instead.",
          };
    } catch {
      outcome = {
        ok: false,
        message: "No connection, so it couldn't be sent. Try again shortly.",
      };
    } finally {
      sending = false;
      onPrinted?.();
    }
  }
</script>

<span class="print-block">
  <span class="print-button">
    {#if chosen}
      <button class="secondary" disabled={sending} onclick={print}
        ><Printer size={18} />{label}</button
      >
    {:else}
      <a
        class="button secondary"
        href={withCopies(href, copies)}
        target="_blank"
        rel="noopener"><Printer size={18} />{label}</a
      >
    {/if}
    <select
      bind:value={copies}
      aria-label="How many copies of the {label.toLowerCase()}"
    >
      {#each [1, 2, 5, 10, 20, 50] as count (count)}
        <option value={count}
          >{count === 1 ? "1 copy" : `${count} copies`}</option
        >
      {/each}
    </select>
  </span>
  {#if chosen}
    <span class="print-choice muted">
      {#if choices.length > 1}
        <label
          >On <select bind:value={chosenId}>
            {#each choices as printer (printer.id)}
              <option value={printer.id}>{printer.name}</option>
            {/each}
          </select></label
        >
      {:else}
        On {chosen.name}.
      {/if}
      <a
        class="small-button"
        href={withCopies(href, copies)}
        target="_blank"
        rel="noopener"><FileDown size={16} aria-hidden="true" /> PDF instead</a
      >
    </span>
  {/if}
  {#if outcome}
    <span class={outcome.ok ? "muted" : "problem"} role="status"
      >{outcome.message}</span
    >
  {/if}
</span>
