<script lang="ts">
  import { manageSections, type ManageSection } from "../lib/sections";
  import type { BatchPrefill } from "../lib/work";
  import ExternalSignIn from "./manage/ExternalSignIn.svelte";
  import Figures from "./manage/Figures.svelte";
  import Locations from "./manage/Locations.svelte";
  import Money from "./manage/Money.svelte";
  import Plants from "./manage/Plants.svelte";
  import PotSizes from "./manage/PotSizes.svelte";
  import Purchases from "./manage/Purchases.svelte";
  import Staff from "./manage/Staff.svelte";
  import Stations from "./manage/Stations.svelte";
  import Suppliers from "./manage/Suppliers.svelte";

  let {
    section,
    onSection,
    onBack,
    onBecameStation,
    onStartBatch,
  }: {
    section: ManageSection | null;
    onSection: (section: ManageSection | null) => void;
    onBack: () => void;
    onBecameStation: () => void;
    onStartBatch: (prefill: BatchPrefill) => void;
  } = $props();

  const back = () => onSection(null);
</script>

{#if section === "figures"}
  <Figures onBack={back} />
{:else if section === "money"}
  <Money onBack={back} />
{:else if section === "purchases"}
  <Purchases onBack={back} {onStartBatch} />
{:else if section === "staff"}
  <Staff onBack={back} />
{:else if section === "plants"}
  <Plants onBack={back} />
{:else if section === "potSizes"}
  <PotSizes onBack={back} />
{:else if section === "suppliers"}
  <Suppliers onBack={back} />
{:else if section === "places"}
  <Locations onBack={back} />
{:else if section === "signIn"}
  <ExternalSignIn onBack={back} />
{:else if section === "stations"}
  <Stations onBack={back} {onBecameStation} />
{:else}
  <main>
    <h1>Manage</h1>
    <div class="stack">
      {#each manageSections as item (item.section)}
        <button
          class="secondary menu-item"
          onclick={() => onSection(item.section)}
        >
          <item.icon size={20} />
          {item.label}
        </button>
      {/each}
    </div>
    <button class="back" onclick={onBack}>Back</button>
  </main>
{/if}
