<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, type Schemas } from "../../lib/types";

  let {
    onBack,
    onBecameStation,
  }: { onBack: () => void; onBecameStation: () => void } = $props();

  let stations = $state<Schemas["StationSummary"][]>([]);
  let name = $state("");
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    stations = (await api.GET("/api/bench/stations")).data ?? [];
  }

  onMount(() => void attempt(load, setProblem));

  const register = (event: SubmitEvent) => {
    event.preventDefault();

    if (
      !confirm("Make this device a bench station? You'll be signed out of it.")
    ) {
      return;
    }

    return attempt(async () => {
      const { response } = await api.POST("/api/bench/stations", {
        body: { name },
      });

      if (response.ok) {
        onBecameStation();
      } else {
        problem = "Please give the station a name.";
      }
    }, setProblem);
  };

  const revoke = (station: Schemas["StationSummary"]) => {
    if (!confirm(`Stop ${station.name} being a bench station?`)) {
      return;
    }

    return attempt(async () => {
      await api.POST("/api/bench/stations/{id}/revoke", {
        params: { path: { id: station.id } },
      });
      await load();
    }, setProblem);
  };
</script>

<main>
  <h1>Bench stations</h1>
  <p>
    A bench station is a shared tablet or computer. Staff tap their name and
    enter their PIN.
  </p>

  <div class="split">
    <section>
      <form class="stack" onsubmit={register}>
        <label>Name for this device <input bind:value={name} required /></label>
        <button type="submit">Make this device a bench station</button>
      </form>
    </section>
    <section>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      {#each stations.filter((station) => !station.revoked) as station (station.id)}
        <div class="card">
          <strong>{station.name}</strong>
          <button class="secondary" onclick={() => revoke(station)}
            >Remove</button
          >
        </div>
      {/each}
    </section>
  </div>

  <button class="secondary mobile-only" onclick={onBack}>Back</button>
</main>
