<script lang="ts">
  import Locations from "./manage/Locations.svelte";
  import Plants from "./manage/Plants.svelte";
  import PotSizes from "./manage/PotSizes.svelte";
  import Staff from "./manage/Staff.svelte";
  import Stations from "./manage/Stations.svelte";
  import Suppliers from "./manage/Suppliers.svelte";

  let {
    onBack,
    onBecameStation,
  }: { onBack: () => void; onBecameStation: () => void } = $props();

  type View =
    "staff" | "plants" | "potSizes" | "suppliers" | "places" | "stations";

  const items: { view: View; label: string }[] = [
    { view: "staff", label: "Staff" },
    { view: "plants", label: "Plants" },
    { view: "potSizes", label: "Pot sizes" },
    { view: "suppliers", label: "Suppliers" },
    { view: "places", label: "Places" },
    { view: "stations", label: "Bench stations" },
  ];

  let view = $state<View | null>(null);
  const back = () => (view = null);
</script>

{#if view === "staff"}
  <Staff onBack={back} />
{:else if view === "plants"}
  <Plants onBack={back} />
{:else if view === "potSizes"}
  <PotSizes onBack={back} />
{:else if view === "suppliers"}
  <Suppliers onBack={back} />
{:else if view === "places"}
  <Locations onBack={back} />
{:else if view === "stations"}
  <Stations onBack={back} {onBecameStation} />
{:else}
  <main>
    <h1>Manage</h1>
    <div class="stack">
      {#each items as item (item.view)}
        <button class="secondary" onclick={() => (view = item.view)}
          >{item.label}</button
        >
      {/each}
    </div>
    <button class="secondary" onclick={onBack}>Back</button>
  </main>
{/if}
