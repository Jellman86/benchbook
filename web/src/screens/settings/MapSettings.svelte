<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt } from "../../lib/types";

  // The picture behind the map under Places: Esri's satellite pictures with a key, a street map without.
  let { onBack }: { onBack: () => void } = $props();

  let hasMapKey = $state(false);
  let mapKey = $state("");
  let problem = $state("");
  let saved = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    hasMapKey = !!(await api.GET("/api/locations/map")).data?.imageryKey;
  }

  onMount(() => void attempt(load, setProblem));

  const saveMapKey = (event: SubmitEvent | null, key: string) => {
    event?.preventDefault();

    return attempt(async () => {
      const { response } = await api.PUT("/api/settings/map", {
        body: { imageryKey: key },
      });
      saved = response.ok
        ? key
          ? "Satellite key saved. The places map uses it now."
          : "Satellite key removed. The places map shows a street map."
        : "";
      problem = response.ok ? "" : "That doesn't look like an Esri key.";
      mapKey = "";
      await load();
    }, setProblem);
  };
</script>

<main>
  <h1>Map</h1>
  <p class="muted">
    The picture behind the map under Places, where the nursery's places are
    drawn.
  </p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if saved}<p class="card done" role="status">{saved}</p>{/if}

  <div class="stack narrow">
    <p class={hasMapKey ? "backup-ok" : ""} role="status">
      {hasMapKey
        ? "Showing satellite pictures from Esri."
        : "Showing a street map from OpenStreetMap."}
    </p>
    <form
      class="form-card stack"
      onsubmit={(event) => saveMapKey(event, mapKey)}
    >
      <h2 class="form-heading">Satellite pictures</h2>
      <p class="muted">
        The map under Places shows satellite pictures from Esri with a free key.
        Without one it shows a street map.
      </p>
      <details>
        <summary>Getting a key</summary>
        <ol>
          <li>
            Sign up for a free ArcGIS Location Platform account at <a
              href="https://location.arcgis.com"
              target="_blank"
              rel="noopener">location.arcgis.com</a
            >.
          </li>
          <li>Create an API key and tick its basemap privileges.</li>
          <li>
            Limit it to this address, {location.origin}, so it can't be used
            elsewhere.
          </li>
          <li>
            Paste it here. The free allowance is far more than a nursery uses.
          </li>
        </ol>
      </details>
      <label
        >Esri API key <input
          bind:value={mapKey}
          type="password"
          autocomplete="off"
          placeholder={hasMapKey ? "Saved. Leave blank to keep it." : ""}
        /></label
      >
      <div class="row">
        <button type="submit" disabled={!mapKey.trim()}>Save key</button>
        {#if hasMapKey}
          <button
            type="button"
            class="secondary"
            onclick={() => saveMapKey(null, "")}>Remove key</button
          >
        {/if}
      </div>
    </form>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
