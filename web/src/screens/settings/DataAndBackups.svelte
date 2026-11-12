<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { momentLabel } from "../../lib/dates";
  import { attempt, type Schemas } from "../../lib/types";
  import ClearRecords from "./ClearRecords.svelte";
  import ImportSpreadsheet from "./ImportSpreadsheet.svelte";

  let { onBack }: { onBack: () => void } = $props();

  let backups = $state<Schemas["BackupHealth"] | null>(null);
  let demo = $state<Schemas["DemoStatus"] | null>(null);
  let loading = $state(false);
  let problem = $state("");
  let saved = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    backups = (await api.GET("/api/backups/status")).data ?? null;
    demo = (await api.GET("/api/demo")).data ?? null;
  }

  onMount(() => void attempt(load, setProblem));

  const loadExampleData = () =>
    attempt(async () => {
      loading = true;

      try {
        const { response } = await api.POST("/api/demo/load");
        problem = response.ok ? "" : "The example data couldn't be loaded.";
        saved = response.ok
          ? "Example data loaded. Have a look round: every screen has something in it now."
          : "";
        await load();
      } finally {
        loading = false;
      }
    }, setProblem);
</script>

<main>
  <h1>Data and backups</h1>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if saved}<p class="card done" role="status">{saved}</p>{/if}

  <div class="split">
    <section>
      <div class="form-card stack">
        <h2 class="form-heading">Backups</h2>
        {#if !backups}
          <p class="muted">Checking…</p>
        {:else if !backups.known}
          <p>GrowLog hasn't heard from its backups yet.</p>
          <p class="muted">
            On a new server that's expected until the first hourly backup has
            run. If it carries on, ask whoever looks after the server.
          </p>
        {:else}
          {#if backups.healthy}
            <p class="backup-ok" role="status">Backups are running.</p>
          {:else}
            <p class="problem" role="alert">
              Backups may have stopped. Records aren't being protected until
              this is fixed, so tell whoever looks after the server.
            </p>
          {/if}
          <dl class="facts">
            <dt>Last backup</dt>
            <dd>
              {backups.lastDumpAt ? momentLabel(backups.lastDumpAt) : "None"}
            </dd>
            <dt>Last copied away</dt>
            <dd>
              {backups.lastTransferAt
                ? momentLabel(backups.lastTransferAt)
                : "None"}
            </dd>
          </dl>
          <p class="muted">
            The server backs GrowLog up and copies the backup to another
            computer. If either is more than a couple of hours late, the home
            screen says so.
          </p>
        {/if}
      </div>

      <div class="form-card stack">
        <h2 class="form-heading">Take everything out</h2>
        <p class="muted">
          Every record in one download: a spreadsheet file for each of plants,
          batches, trays, all the work done, purchases, invoices, passports,
          consignments and more. Photos stay in the backups.
        </p>
        <a class="button secondary" href="/api/export" download
          >Download everything</a
        >
      </div>
    </section>

    <section class="stack">
      <ImportSpreadsheet
        onDone={(message) => {
          saved = message;
          problem = "";
        }}
      />
      {#if demo?.canClear}
        <div class="form-card stack">
          <h2 class="form-heading">Example data</h2>
          <p class="muted">
            Made-up plants, batches, places, suppliers, purchases and invoices,
            so you can see every screen filled in before real records go in.
          </p>
          {#if demo.available}
            <button disabled={loading} onclick={loadExampleData}
              >Load example data</button
            >
          {:else}
            <p>
              GrowLog already has plants or batches in it, so example data can't
              be added. Start afresh below to empty it first.
            </p>
          {/if}
        </div>

        <ClearRecords />
      {:else if demo}
        <p class="muted">
          GrowLog is keeping real records, so example data and starting afresh
          are switched off. Plant passport records have to be kept for three
          years.
        </p>
      {/if}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
