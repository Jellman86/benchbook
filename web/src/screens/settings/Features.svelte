<script lang="ts">
  import {
    featureInfo,
    isOn,
    loadFeatures,
    setFeature,
    type Feature,
  } from "../../lib/features.svelte";
  import { onMount } from "svelte";

  // Parts of GrowLog a nursery may not use. Off hides them everywhere for everyone; their records are kept, so
  // turning one back on brings it all back.
  let { onBack }: { onBack: () => void } = $props();

  let busy = $state<Feature | null>(null);
  let problem = $state("");

  onMount(() => void loadFeatures());

  async function toggle(feature: Feature, on: boolean) {
    busy = feature;
    problem = "";

    try {
      if (!(await setFeature(feature, on))) {
        problem = "That couldn't be changed. Please try again.";
      }
    } catch {
      problem = "Can't reach GrowLog. Check the connection and try again.";
    } finally {
      busy = null;
    }
  }
</script>

<main>
  <h1>Features</h1>
  <p class="muted">
    Turn off what the nursery doesn't use, so nobody sees it. It's hidden
    everywhere, for everyone, until it's turned back on. Nothing recorded is
    lost.
  </p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  <ul class="plain-list feature-list">
    {#each featureInfo as item (item.feature)}
      {@const on = isOn(item.feature)}
      <li class="card feature-switch">
        <label>
          <input
            type="checkbox"
            role="switch"
            checked={on}
            disabled={busy === item.feature}
            onchange={(event) =>
              toggle(item.feature, event.currentTarget.checked)}
          />
          <span class="stack tight">
            <strong>{item.label}</strong>
            <span class="muted"
              >{on ? "On. Turning it off hides:" : "Off, so hidden:"}
              {item.hides}</span
            >
          </span>
        </label>
      </li>
    {/each}
  </ul>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
