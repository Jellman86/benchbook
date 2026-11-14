<script lang="ts">
  import ExternalLink from "@lucide/svelte/icons/external-link";
  import Trash2 from "@lucide/svelte/icons/trash-2";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { shrinkPhoto } from "../../lib/photo";
  import { attempt } from "../../lib/types";

  // What places are drawn on: Esri's satellite pictures with a key, a street map without, or the nursery's own
  // picture, such as an aerial photo or a plan.
  let { onBack }: { onBack: () => void } = $props();

  // Big enough to zoom into a greenhouse on an aerial photo, small enough to load quickly on a phone.
  const pictureSide = 4096;

  let hasMapKey = $state(false);
  let onPicture = $state(false);
  let pictureChangedAt = $state<string | null>(null);
  let busy = $state(false);
  let mapKey = $state("");
  let problem = $state("");
  let saved = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    const { data } = await api.GET("/api/locations/map");
    hasMapKey = !!data?.imageryKey;
    onPicture = !!data?.onPicture;
    pictureChangedAt = data?.pictureChangedAt ?? null;
  }

  const upload = (event: Event) =>
    attempt(async () => {
      const input = event.currentTarget as HTMLInputElement;
      const file = input.files?.[0];
      input.value = "";

      if (!file) {
        return;
      }

      if (
        pictureChangedAt &&
        !confirm(
          "Replace the picture? Places drawn on it keep their outlines, so if the new one shows the nursery differently, redraw them.",
        )
      ) {
        return;
      }

      busy = true;

      try {
        const response = await fetch("/api/settings/map/picture", {
          method: "PUT",
          body: await shrinkPhoto(file, pictureSide),
        });
        problem = response.ok
          ? ""
          : "That picture couldn't be used. Use a JPEG, PNG or WebP photo or plan.";
        saved = response.ok
          ? "The picture has been saved, and places are drawn on it now."
          : "";
        await load();
      } finally {
        busy = false;
      }
    }, setProblem);

  const usePicture = (use: boolean) =>
    attempt(async () => {
      await api.PUT("/api/settings/map/use-picture", { body: { use } });
      saved = use
        ? "Places are drawn on your picture."
        : "Places are drawn on the map.";
      await load();
    }, setProblem);

  const removePicture = () => {
    if (
      !confirm(
        "Remove the picture? Places drawn on it stay recorded, and show again if a picture is added.",
      )
    ) {
      return;
    }

    return attempt(async () => {
      await api.DELETE("/api/settings/map/picture");
      saved = "The picture has been removed, so places are drawn on the map.";
      await load();
    }, setProblem);
  };

  onMount(() => void attempt(load, setProblem));

  const saveMapKey = (event: SubmitEvent | null, key: string) => {
    event?.preventDefault();

    return attempt(async () => {
      const { response } = await api.PUT("/api/settings/map", {
        body: { imageryKey: key },
      });
      saved = response.ok
        ? key
          ? "The satellite key has been saved, and the places map uses it now."
          : "The satellite key has been removed, so the places map shows a street map."
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
    This sets the picture behind the map under Places, where the nursery's
    places are drawn.
  </p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if saved}<p class="card done" role="status">{saved}</p>{/if}

  <div class="stack narrow">
    <p class={hasMapKey || onPicture ? "backup-ok" : ""} role="status">
      {onPicture
        ? "Places are drawn on your own picture."
        : hasMapKey
          ? "The map is showing satellite pictures from Esri."
          : "The map is showing a street map from OpenStreetMap. Add a satellite key or your own picture to draw around greenhouses."}
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
            Sign up for a free ArcGIS Location Platform account.
            <a
              class="small-button"
              href="https://location.arcgis.com"
              target="_blank"
              rel="noopener"
              >Go to ArcGIS <ExternalLink size={16} aria-hidden="true" /></a
            >
          </li>
          <li>
            Create an API key and tick its basemap privileges, and geocoding to
            find places by address.
          </li>
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

    <div class="form-card stack">
      <h2 class="form-heading">Your own picture</h2>
      <p class="muted">
        No key? Upload an aerial photo, a drone shot or a plan of the nursery
        instead, and draw places on that. Places drawn on the picture and on the
        map are kept apart, as the two don't line up.
      </p>
      {#if pictureChangedAt}
        <img
          class="map-picture-preview"
          src="/api/settings/map/picture?v={encodeURIComponent(
            pictureChangedAt,
          )}"
          alt="The nursery from above, as places are drawn on it"
        />
        <fieldset class="day-picker">
          <legend class="field-label">Draw places on</legend>
          <div class="segmented two">
            <button
              type="button"
              aria-pressed={!onPicture}
              class:secondary={onPicture}
              onclick={() => usePicture(false)}
              >{hasMapKey ? "Satellite map" : "Street map"}</button
            >
            <button
              type="button"
              aria-pressed={onPicture}
              class:secondary={!onPicture}
              onclick={() => usePicture(true)}>This picture</button
            >
          </div>
        </fieldset>
      {/if}
      <label
        >{pictureChangedAt ? "Replace the picture" : "Upload a picture"}
        <input
          type="file"
          accept="image/jpeg,image/png,image/webp"
          disabled={busy}
          onchange={upload}
        /></label
      >
      {#if pictureChangedAt}
        <button type="button" class="link-button" onclick={removePicture}
          ><Trash2 size={16} aria-hidden="true" />Remove the picture</button
        >
      {/if}
    </div>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
