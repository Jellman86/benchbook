<script lang="ts">
  import FormPanel from "../FormPanel.svelte";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, type Schemas } from "../../lib/types";

  let { onBack }: { onBack: () => void } = $props();

  let sizes = $state<Schemas["NamedEntry"][]>([]);
  let name = $state("");
  let problem = $state("");
  let renaming = $state<string | null>(null);
  let newName = $state("");

  const setProblem = (message: string) => (problem = message);

  function explain(status: number) {
    problem =
      status === 409
        ? "That pot size is already on the list."
        : "Please enter a name.";
  }

  async function load() {
    sizes = (await api.GET("/api/pot-sizes")).data ?? [];
  }

  onMount(() => void attempt(load, setProblem));

  const add = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/pot-sizes", {
        body: { name },
      });

      if (response.ok) {
        name = "";
        await load();
      } else {
        explain(response.status);
      }
    }, setProblem);
  };

  const rename = (event: SubmitEvent, id: string) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.PUT("/api/pot-sizes/{id}", {
        params: { path: { id } },
        body: { name: newName },
      });

      if (response.ok) {
        renaming = null;
        await load();
      } else {
        explain(response.status);
      }
    }, setProblem);
  };

  const archive = (id: string) =>
    attempt(async () => {
      await api.POST("/api/pot-sizes/{id}/archive", {
        params: { path: { id } },
      });
      await load();
    }, setProblem);
</script>

<main>
  <h1>Pot sizes</h1>

  <div class="split">
    <section>
      <FormPanel label="Add a pot size">
        <form class="stack" onsubmit={add}>
          <label>New pot size <input bind:value={name} required /></label>
          <button type="submit">Add</button>
        </form>
      </FormPanel>
    </section>
    <section>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      {#each sizes as size (size.id)}
        <div class="card">
          {#if renaming === size.id}
            <form class="stack" onsubmit={(event) => rename(event, size.id)}>
              <label>Name <input bind:value={newName} required /></label>
              <button type="submit">Save</button>
            </form>
          {:else}
            <strong>{size.name}</strong>
            <div class="row">
              <button
                class="secondary"
                onclick={() => {
                  renaming = size.id;
                  newName = size.name;
                }}>Rename</button
              >
              <button class="secondary" onclick={() => archive(size.id)}
                >No longer used</button
              >
            </div>
          {/if}
        </div>
      {/each}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
