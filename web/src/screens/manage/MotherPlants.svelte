<script lang="ts">
  import { showForm } from "../../lib/showForm";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { dayLabel } from "../../lib/dates";
  import { attempt, optional, type Schemas } from "../../lib/types";
  import { plantName, workPlaces } from "../../lib/work";

  // Plants kept for taking cuttings from, each with every batch taken from it, in order.
  let {
    onBack,
    onTrace,
  }: { onBack: () => void; onTrace: (batchId: string) => void } = $props();

  type Mother = Schemas["MotherSummary"];

  let mothers = $state<Mother[]>([]);
  let plants = $state<Schemas["PlantResponse"][]>([]);
  let places = $state<{ id: string; label: string }[]>([]);
  let opened = $state<Schemas["MotherDetails"] | null>(null);
  let form = $state({ plantId: "", name: "", locationId: "", notes: "" });
  let editing = $state<string | null>(null);
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    mothers = (await api.GET("/api/mother-plants")).data ?? [];
  }

  onMount(() =>
    attempt(async () => {
      plants = ((await api.GET("/api/plants")).data ?? []).filter(
        (plant) => !plant.archived,
      );
      places = workPlaces((await api.GET("/api/locations")).data ?? []);
      await load();
    }, setProblem),
  );

  const open = (mother: Mother) =>
    attempt(async () => {
      opened =
        (
          await api.GET("/api/mother-plants/{id}", {
            params: { path: { id: mother.id } },
          })
        ).data ?? null;
    }, setProblem);

  function edit(mother: Mother) {
    editing = mother.id;
    void showForm();
    form = {
      plantId: mother.plantId,
      name: mother.name,
      locationId: mother.locationId ?? "",
      notes: mother.notes ?? "",
    };
  }

  function reset() {
    editing = null;
    form = { plantId: "", name: "", locationId: "", notes: "" };
  }

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const body = {
        plantId: form.plantId,
        name: form.name,
        locationId: form.locationId || null,
        notes: optional(form.notes),
      };
      const { response } = editing
        ? await api.PUT("/api/mother-plants/{id}", {
            params: { path: { id: editing } },
            body,
          })
        : await api.POST("/api/mother-plants", { body });

      if (response.ok) {
        problem = "";
        reset();
        await load();
      } else {
        problem =
          response.status === 409
            ? "That plant already has a mother with that name, or its cuttings belong to another plant."
            : "Choose the plant and give the mother a name.";
      }
    }, setProblem);
  };

  const archive = (mother: Mother) => {
    if (
      !confirm(`${mother.name} is no longer used? Its cuttings stay recorded.`)
    ) {
      return;
    }

    return attempt(async () => {
      await api.POST("/api/mother-plants/{id}/archive", {
        params: { path: { id: mother.id } },
      });
      opened = null;
      await load();
    }, setProblem);
  };
</script>

<main>
  <h1>Mother plants</h1>
  <p class="muted">
    Plants kept for cuttings. Each batch of cuttings taken from one is numbered
    in turn, and leads back to it.
  </p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  <div class="split">
    <section>
      <form class="form-card stack" onsubmit={save} data-edit-form>
        <h2 class="form-heading">
          {editing ? "Change the mother plant" : "Add a mother plant"}
        </h2>
        <label
          >Plant <select bind:value={form.plantId} required>
            <option value="">Choose…</option>
            {#each plants as plant (plant.id)}
              <option value={plant.id}
                >{plantName(plant.commonName, plant.variety)}</option
              >
            {/each}
          </select></label
        >
        <label
          >Name <input
            bind:value={form.name}
            required
            maxlength="100"
            placeholder="By the door"
          /></label
        >
        <label
          >Where it is (optional) <select bind:value={form.locationId}>
            <option value="">Not set</option>
            {#each places as place (place.id)}
              <option value={place.id}>{place.label}</option>
            {/each}
          </select></label
        >
        <label
          >Notes (optional) <textarea bind:value={form.notes} rows="2"
          ></textarea></label
        >
        <button type="submit">{editing ? "Save" : "Add mother plant"}</button>
        {#if editing}
          <button type="button" class="secondary" onclick={reset}>Cancel</button
          >
        {/if}
      </form>
    </section>

    <section class="stack">
      {#each mothers as mother (mother.id)}
        <div class="card stack tight">
          <strong>{mother.name}</strong>
          <span class="muted"
            >{mother.plantName}{mother.locationName
              ? ` · ${mother.locationName}`
              : ""}</span
          >
          <span
            >{mother.cuttingsTaken === 0
              ? "No cuttings taken yet"
              : `${mother.cuttingsTaken} batch${Number(mother.cuttingsTaken) === 1 ? "" : "es"} of cuttings${mother.lastTakenAt ? `, last ${dayLabel(mother.lastTakenAt.slice(0, 10))}` : ""}`}</span
          >
          <div class="row">
            <button class="secondary" onclick={() => open(mother)}
              >Cuttings taken</button
            >
            <button class="secondary" onclick={() => edit(mother)}
              >Change</button
            >
            <button class="secondary" onclick={() => archive(mother)}
              >No longer used</button
            >
          </div>
          {#if opened?.id === mother.id}
            <ol class="timeline">
              {#each opened.batches as batch (batch.batchId)}
                <li>
                  <span class="timeline-what"
                    >Cuttings {batch.round}: batch {batch.traceabilityCode}</span
                  >
                  <span class="muted credit"
                    >{dayLabel(batch.startedAt.slice(0, 10))}</span
                  >
                  <button
                    class="link-button"
                    onclick={() => onTrace(batch.batchId)}>Trace</button
                  >
                </li>
              {:else}
                <li><span class="muted">None yet.</span></li>
              {/each}
            </ol>
          {/if}
        </div>
      {:else}
        <p class="muted">No mother plants yet.</p>
      {/each}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
