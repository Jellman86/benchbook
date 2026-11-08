<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, type Schemas } from "../../lib/types";

  let { onBack }: { onBack: () => void } = $props();

  type Location = Schemas["LocationResponse"];
  type Kind = Schemas["LocationKind"];

  const kinds: Kind[] = ["Site", "Greenhouse", "Polytunnel", "Bench", "Bay"];

  const parentKinds: Record<Kind, Kind[]> = {
    Site: [],
    Greenhouse: ["Site"],
    Polytunnel: ["Site"],
    Bench: ["Greenhouse", "Polytunnel"],
    Bay: ["Greenhouse", "Polytunnel"],
  };

  let locations = $state<Location[]>([]);
  let name = $state("");
  let kind = $state<Kind>("Site");
  let parentId = $state("");
  let problem = $state("");
  let renaming = $state<string | null>(null);
  let newName = $state("");

  const parents = $derived(
    locations.filter((location) => parentKinds[kind].includes(location.kind)),
  );

  const setProblem = (message: string) => (problem = message);

  function explain(status: number) {
    problem =
      status === 409
        ? "There's already a place with that name there."
        : "Please check the name and where it belongs.";
  }

  async function load() {
    locations = (await api.GET("/api/locations")).data ?? [];
  }

  onMount(() => void attempt(load, setProblem));

  const add = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/locations", {
        body: { name, kind, parentId: kind === "Site" ? null : parentId },
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
      const { response } = await api.PUT("/api/locations/{id}", {
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
      await api.POST("/api/locations/{id}/archive", {
        params: { path: { id } },
      });
      await load();
    }, setProblem);
</script>

<main>
  <h1>Places</h1>

  <div class="split">
    <section>
      <form class="stack" onsubmit={add}>
        <label>
          Type
          <select bind:value={kind}>
            {#each kinds as option (option)}<option value={option}
                >{option}</option
              >{/each}
          </select>
        </label>
        {#if kind !== "Site"}
          <label>
            Inside
            <select bind:value={parentId} required>
              <option value="">Choose…</option>
              {#each parents as parent (parent.id)}
                <option value={parent.id}>{parent.name}</option>
              {/each}
            </select>
          </label>
        {/if}
        <label>Name <input bind:value={name} required /></label>
        <button type="submit">Add</button>
      </form>
    </section>
    <section>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      {#each locations as location (location.id)}
        <div class="card">
          {#if renaming === location.id}
            <form
              class="stack"
              onsubmit={(event) => rename(event, location.id)}
            >
              <label>Name <input bind:value={newName} required /></label>
              <button type="submit">Save</button>
            </form>
          {:else}
            <strong>{location.name}</strong>
            <span class="muted">{location.kind}</span>
            <div class="row">
              <button
                class="secondary"
                onclick={() => {
                  renaming = location.id;
                  newName = location.name;
                }}>Rename</button
              >
              <button class="secondary" onclick={() => archive(location.id)}
                >No longer used</button
              >
            </div>
          {/if}
        </div>
      {/each}
    </section>
  </div>

  <button class="secondary mobile-only" onclick={onBack}>Back</button>
</main>
