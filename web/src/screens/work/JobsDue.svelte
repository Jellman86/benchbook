<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { dayLabel } from "../../lib/dates";
  import type { Schemas } from "../../lib/types";

  // Jobs due today or late, to tick off; and for the owner, anything in the records that wants a look, with why.
  let {
    isOwner,
    onGo,
  }: {
    isOwner: boolean;
    onGo: (where: string) => void;
  } = $props();

  let jobs = $state<Schemas["JobSummary"][]>([]);
  let alerts = $state<Schemas["Alert"][]>([]);
  let busyId = $state<string | null>(null);
  let problem = $state("");

  const due = $derived(jobs.filter((job) => job.due));

  async function load() {
    try {
      jobs = (await api.GET("/api/jobs")).data ?? [];

      if (isOwner) {
        alerts = (await api.GET("/api/alerts")).data ?? [];
      }
    } catch {
      // No signal: the jobs show again when there is one.
    }
  }

  onMount(load);

  async function done(job: Schemas["JobSummary"]) {
    busyId = job.id;
    problem = "";

    try {
      const { response } = await api.POST("/api/jobs/{id}/done", {
        params: { path: { id: job.id } },
        body: { doneOn: null },
      });
      problem = response.ok ? "" : "That couldn't be marked done.";
      await load();
    } catch {
      problem = "Marking a job done needs a signal.";
    } finally {
      busyId = null;
    }
  }

  const where: Record<string, string> = {
    nursery: "Nursery details",
    invoices: "Invoices",
    production: "Production",
    jobs: "Jobs",
  };
</script>

{#if alerts.length > 0}
  <h2>Needs a look</h2>
  {#each alerts as alert (alert.kind)}
    <div class="card alert-card">
      <strong>{alert.title}</strong>
      <span class="muted">{alert.why}</span>
      <button class="link-button" onclick={() => onGo(alert.where)}
        >Open {where[alert.where] ?? alert.where}</button
      >
    </div>
  {/each}
{/if}

{#if due.length > 0}
  <h2>Jobs due</h2>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#each due as job (job.id)}
    <div class="card recent">
      <span class="plant-names">
        <strong>{job.title}</strong>
        <em
          >{job.locationName ? `${job.locationName} · ` : ""}{Number(
            job.daysLate,
          ) > 0
            ? `${job.daysLate} day${Number(job.daysLate) === 1 ? "" : "s"} late`
            : "Due today"}{job.lastDoneOn
            ? ` · last done ${dayLabel(job.lastDoneOn)}${job.lastDoneBy ? ` by ${job.lastDoneBy}` : ""}`
            : ""}</em
        >
      </span>
      <div class="row">
        <button
          class="secondary"
          disabled={busyId === job.id}
          onclick={() => done(job)}>Done</button
        >
      </div>
    </div>
  {/each}
{/if}
