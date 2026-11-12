<script lang="ts">
  import { showForm } from "../../lib/showForm";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { dayLabel } from "../../lib/dates";
  import { isoDay } from "../../lib/days";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import { workPlaces } from "../../lib/work";
  import DayPicker from "../DayPicker.svelte";

  // Repeating jobs: watering, feeding and spraying rotas. Anyone ticks them off on Record; they're set up here.
  let { onBack }: { onBack: () => void } = $props();

  type Job = Schemas["JobSummary"];

  const empty = () => ({
    title: "",
    notes: "",
    every: 7,
    unit: "days" as "days" | "weeks",
    nextDue: isoDay(new Date()),
    locationId: "",
  });

  let jobs = $state<Job[]>([]);
  let places = $state<{ id: string; label: string }[]>([]);
  let form = $state(empty());
  let editing = $state<string | null>(null);
  let problem = $state("");
  let saved = $state("");

  const setProblem = (message: string) => (problem = message);
  const everyDays = $derived(
    form.unit === "weeks" ? Number(form.every) * 7 : Number(form.every),
  );

  async function load() {
    jobs = (await api.GET("/api/jobs")).data ?? [];
  }

  onMount(() =>
    attempt(async () => {
      places = workPlaces((await api.GET("/api/locations")).data ?? []);
      await load();
    }, setProblem),
  );

  function edit(job: Job) {
    editing = job.id;
    void showForm();
    const weeks = Number(job.everyDays) % 7 === 0;
    form = {
      title: job.title,
      notes: job.notes ?? "",
      every: weeks ? Number(job.everyDays) / 7 : Number(job.everyDays),
      unit: weeks ? "weeks" : "days",
      nextDue: job.nextDue,
      locationId: job.locationId ?? "",
    };
    saved = "";
  }

  function reset() {
    editing = null;
    form = empty();
  }

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const body = {
        title: form.title,
        notes: optional(form.notes),
        everyDays,
        firstDue: form.nextDue,
        locationId: form.locationId || null,
      };
      const { response } = editing
        ? await api.PUT("/api/jobs/{id}", {
            params: { path: { id: editing } },
            body,
          })
        : await api.POST("/api/jobs", { body });

      if (response.ok) {
        saved = `${form.title.trim()} saved.`;
        problem = "";
        reset();
        await load();
      } else {
        problem =
          "Give the job a name, and how often: once a day to once a year.";
      }
    }, setProblem);
  };

  const stop = (job: Job) => {
    if (
      !confirm(
        `Stop "${job.title}"? It won't come up again; what was done stays recorded.`,
      )
    ) {
      return;
    }

    return attempt(async () => {
      await api.POST("/api/jobs/{id}/stop", {
        params: { path: { id: job.id } },
      });

      if (editing === job.id) {
        reset();
      }

      await load();
    }, setProblem);
  };

  const how = (job: Job) => {
    const days = Number(job.everyDays);
    return days === 1
      ? "Every day"
      : days % 7 === 0
        ? days === 7
          ? "Every week"
          : `Every ${days / 7} weeks`
        : `Every ${days} days`;
  };
</script>

<main>
  <h1>Jobs</h1>
  <p class="muted">
    Jobs that come round again, such as watering, feeding or spraying. They show
    on Record when due, and anyone can tick them off; the next date moves on by
    itself.
  </p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if saved}<p class="card done" role="status">{saved}</p>{/if}

  <div class="split">
    <section>
      <form class="form-card stack" onsubmit={save} data-edit-form>
        <h2 class="form-heading">{editing ? "Change the job" : "Add a job"}</h2>
        <label
          >What <input
            bind:value={form.title}
            required
            maxlength="120"
            placeholder="Water house 1"
          /></label
        >
        <div class="field-grid">
          <label
            >Every <input
              type="number"
              min="1"
              max="365"
              inputmode="numeric"
              bind:value={form.every}
              required
            /></label
          >
          <label
            >&nbsp;<select bind:value={form.unit}>
              <option value="days">days</option>
              <option value="weeks">weeks</option>
            </select></label
          >
        </div>
        <DayPicker bind:value={form.nextDue} label="Next due" max={null} />
        <label
          >Where (optional) <select bind:value={form.locationId}>
            <option value="">Anywhere</option>
            {#each places as place (place.id)}
              <option value={place.id}>{place.label}</option>
            {/each}
          </select></label
        >
        <label
          >Notes (optional) <textarea
            bind:value={form.notes}
            rows="2"
            maxlength="500"></textarea></label
        >
        <button type="submit" disabled={everyDays < 1 || everyDays > 365}
          >{editing ? "Save" : "Add job"}</button
        >
        {#if editing}
          <button type="button" class="secondary" onclick={reset}>Cancel</button
          >
        {/if}
      </form>
    </section>

    <section class="stack">
      {#each jobs as job (job.id)}
        <div class="card stack tight">
          <strong>{job.title}</strong>
          <span class="muted"
            >{how(job)}{job.locationName ? ` · ${job.locationName}` : ""} · next
            {dayLabel(job.nextDue)}{job.due ? " (due)" : ""}</span
          >
          {#if job.lastDoneOn}
            <span class="muted"
              >Last done {dayLabel(job.lastDoneOn)}{job.lastDoneBy
                ? ` by ${job.lastDoneBy}`
                : ""}</span
            >
          {/if}
          <div class="row">
            <button class="secondary" onclick={() => edit(job)}>Change</button>
            <button class="secondary" onclick={() => stop(job)}>Stop</button>
          </div>
        </div>
      {:else}
        <p class="muted">No jobs yet.</p>
      {/each}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
