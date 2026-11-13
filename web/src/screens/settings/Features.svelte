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

  async function toggle(input: HTMLInputElement, feature: Feature) {
    busy = feature;
    problem = "";

    try {
      if (!(await setFeature(feature, input.checked))) {
        problem = "That couldn't be changed. Please try again.";
      }
    } catch {
      problem = "Can't reach GrowLog. Check the connection and try again.";
    } finally {
      busy = null;
      // Shows what's really saved, whether or not the change went through.
      input.checked = isOn(feature);
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
            aria-describedby="feature-{item.feature}"
            disabled={busy !== null}
            onchange={(event) => toggle(event.currentTarget, item.feature)}
          />
          <span class="stack tight">
            <strong>{item.label}</strong>
            <span class="muted" id="feature-{item.feature}"
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
