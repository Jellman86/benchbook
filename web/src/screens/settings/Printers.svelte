<script lang="ts">
  import { isOn } from "../../lib/features.svelte";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import {
    forgetPrinters,
    languages,
    printableKinds,
    printProblems,
    type LabelKind,
    type PrinterDetails,
  } from "../../lib/printing";
  import { attempt, type Schemas } from "../../lib/types";

  // Network label printers that Hortiplanner sends labels straight to. A PDF through the print window always works too.
  let { onBack, onLabels }: { onBack: () => void; onLabels: () => void } =
    $props();

  type Form = Schemas["PrinterRequest"];

  const empty = (): Form => ({
    name: "",
    address: "",
    port: 9100,
    language: "Zpl",
    dotsPerInch: 300,
    labels: ["PlantLabel", "ContainerLabel"],
  });

  let list = $state<PrinterDetails[]>([]);
  let form = $state<Form>(empty());
  let editingId = $state<string | null>(null);
  let problem = $state("");
  let saved = $state("");
  let busyId = $state<string | null>(null);
  let results = $state<Record<string, { ok: boolean; message: string }>>({});

  const setProblem = (message: string) => (problem = message);

  async function load() {
    list = (await api.GET("/api/printers")).data ?? [];
    forgetPrinters();
  }

  onMount(() => void attempt(load, setProblem));

  function toggle(kind: LabelKind, on: boolean) {
    form.labels = on
      ? [...form.labels, kind]
      : form.labels.filter((item) => item !== kind);
  }

  function edit(printer: PrinterDetails) {
    editingId = printer.id;
    form = {
      name: printer.name,
      address: printer.address,
      port: printer.port,
      language: printer.language,
      dotsPerInch: printer.dotsPerInch,
      labels: [...printer.labels],
    };
    saved = "";
    problem = "";
  }

  function stopEditing() {
    editingId = null;
    form = empty();
  }

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const body = { ...$state.snapshot(form), port: Number(form.port) };
      const { response } = editingId
        ? await api.PUT("/api/printers/{id}", {
            params: { path: { id: editingId } },
            body,
          })
        : await api.POST("/api/printers", { body });

      if (response.ok) {
        saved = `${form.name.trim()} saved.`;
        problem = "";
        stopEditing();
        await load();
      } else {
        saved = "";
        problem =
          response.status === 409
            ? "There's already a printer with that name."
            : "Check the address: an IP address such as 192.168.1.50, or the printer's network name, without http://.";
      }
    }, setProblem);
  };

  const testPrint = (printer: PrinterDetails) =>
    attempt(async () => {
      busyId = printer.id;

      try {
        const { response, error } = await api.POST("/api/printers/{id}/test", {
          params: { path: { id: printer.id } },
        });
        results[printer.id] = response.ok
          ? {
              ok: true,
              message:
                "Test label sent. If it prints off centre or across two labels, check the size under Labels matches the roll.",
            }
          : {
              ok: false,
              message:
                printProblems[
                  (error as { problem?: string } | undefined)?.problem ?? ""
                ] ?? "That didn't print.",
            };
      } finally {
        busyId = null;
      }
    }, setProblem);

  const remove = (printer: PrinterDetails) => {
    if (
      !confirm(
        `Remove ${printer.name}? Its labels will print as PDFs until another printer takes them.`,
      )
    ) {
      return;
    }

    return attempt(async () => {
      await api.DELETE("/api/printers/{id}", {
        params: { path: { id: printer.id } },
      });

      if (editingId === printer.id) {
        stopEditing();
      }

      await load();
    }, setProblem);
  };

  const prints = (printer: PrinterDetails) =>
    printableKinds
      .filter((item) => printer.labels.includes(item.kind))
      .map((item) => item.label.toLowerCase())
      .join(", ") || "nothing yet";
</script>

<main>
  <h1>Printers</h1>
  <p class="muted">
    Label printers on the nursery's network. Hortiplanner sends labels straight
    to them, and every label can still be printed as a PDF through the print
    window. What each label looks like, and its size, is under <button
      class="link-button"
      onclick={onLabels}>Labels</button
    >.
  </p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if saved}<p class="card done" role="status">{saved}</p>{/if}

  <div class="split">
    <section class="stack">
      <form class="form-card stack" onsubmit={save}>
        <h2 class="form-heading">
          {editingId ? `Change ${form.name || "printer"}` : "Add a printer"}
        </h2>
        <label
          >Name <input
            bind:value={form.name}
            required
            placeholder="Potting shed"
          /></label
        >
        <div class="field-grid">
          <label
            >IP address or network name <input
              bind:value={form.address}
              required
              autocomplete="off"
              autocapitalize="none"
              inputmode="url"
              placeholder="192.168.1.50"
            /></label
          >
          <label
            >Port <input
              bind:value={form.port}
              type="number"
              min="1"
              max="65535"
              inputmode="numeric"
              required
            /></label
          >
        </div>
        <label
          >Printer language <select bind:value={form.language}>
            {#each languages as item (item.language)}
              <option value={item.language}>{item.label}</option>
            {/each}
          </select></label
        >
        <label
          >Print head <select bind:value={form.dotsPerInch}>
            <option value={203}>203 dots an inch</option>
            <option value={300}>300 dots an inch</option>
            <option value={600}>600 dots an inch</option>
          </select></label
        >
        <fieldset class="stack tight">
          <legend class="field-label">Prints</legend>
          <div class="tick-row">
            {#each printableKinds.filter((item) => item.kind !== "PlantPassport" || isOn("Passports")) as item (item.kind)}
              <label class="tick">
                <input
                  type="checkbox"
                  checked={form.labels.includes(item.kind)}
                  onchange={(event) =>
                    toggle(item.kind, event.currentTarget.checked)}
                />
                {item.label}
              </label>
            {/each}
          </div>
        </fieldset>
        <div class="row">
          <button type="submit">{editingId ? "Save" : "Add printer"}</button>
          {#if editingId}
            <button type="button" class="secondary" onclick={stopEditing}
              >Cancel</button
            >
          {/if}
        </div>
      </form>

      <details class="form-card printing-tips">
        <summary>Setting a printer up</summary>
        <ol>
          <li>
            <strong>Give it a fixed address.</strong> In the router, reserve an IP
            address for the printer so it doesn't change. Most printers print their
            address on a settings label when you hold the feed button.
          </li>
          <li>
            <strong>Check its language.</strong> Zebra printers use ZPL. TSC printers,
            and many others such as Xprinter, iDPRT and Munbyn, use TSPL. The manual
            or the settings label says which; many printers understand both.
          </li>
          <li>
            <strong>Leave the port at 9100</strong> unless the manual specifies 9101,
            9102, 515 or 631.
          </li>
          <li>
            <strong>Match the head.</strong> The settings label or the model name
            says 203 or 300 dots an inch. A wrong choice prints labels too big or
            too small.
          </li>
          <li>
            <strong>Press Test print.</strong> Hortiplanner's server must be able
            to reach the printer, so it needs to be on the same network, or connected
            to it.
          </li>
        </ol>
      </details>
    </section>

    <section class="stack">
      {#each list as printer (printer.id)}
        <div class="card stack">
          <strong>{printer.name}</strong>
          <span class="muted"
            >{printer.address}:{printer.port} · {printer.language.toUpperCase()}
            · {printer.dotsPerInch} dpi</span
          >
          <span>Prints {prints(printer)}.</span>
          <div class="row">
            <button
              disabled={busyId === printer.id}
              onclick={() => testPrint(printer)}>Test print</button
            >
            <button class="secondary" onclick={() => edit(printer)}
              >Change</button
            >
            <button class="secondary" onclick={() => remove(printer)}
              >Remove</button
            >
          </div>
          {#if results[printer.id]}
            <p
              class={results[printer.id].ok ? "muted" : "problem"}
              role="status"
            >
              {results[printer.id].message}
            </p>
          {/if}
        </div>
      {:else}
        <p class="note-empty muted">
          No printers yet. Until one is added, labels open as PDFs to print
          through the print window.
        </p>
      {/each}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
