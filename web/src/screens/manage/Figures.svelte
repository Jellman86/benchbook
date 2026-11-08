<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, type Schemas } from "../../lib/types";
  import { lossReasons, percent, plantName } from "../../lib/work";

  let { onBack }: { onBack: () => void } = $props();

  let figures = $state<Schemas["Figures"] | null>(null);
  let problem = $state("");

  const setProblem = (message: string) => (problem = message);

  const reasonLabel = (reason: string) =>
    lossReasons.find((item) => item.reason === reason)?.label ?? reason;

  onMount(() =>
    attempt(async () => {
      figures = (await api.GET("/api/figures")).data ?? null;
    }, setProblem),
  );
</script>

<main>
  <h1>Figures</h1>

  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  {#if figures}
    {#each figures.plants as plant (plant.plantId)}
      <div class="card">
        <strong>{plantName(plant.commonName, plant.variety)}</strong>
        <span>Started: {plant.started}</span>
        <span>Germination: {percent(plant.germinationRate)}</span>
        <span>Survival: {percent(plant.survivalRate)}</span>
        <span>Lost: {plant.lost}</span>
        {#if Number(plant.adjusted) !== 0}
          <span>Stock count adjustments: {plant.adjusted}</span>
        {/if}
        <span>Alive now: {plant.alive}</span>
      </div>
    {:else}
      <p class="muted">Nothing recorded yet.</p>
    {/each}

    {#if figures.losses.length > 0}
      <h2>Losses by reason</h2>
      {#each figures.losses as loss (loss.reason)}
        <div class="card">
          <span>{reasonLabel(String(loss.reason))}: {loss.quantity}</span>
        </div>
      {/each}
    {/if}
  {/if}

  <button class="secondary" onclick={onBack}>Back</button>
</main>
