<script lang="ts">
  import ImageUp from "@lucide/svelte/icons/image-up";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import {
    kindsOfLabel,
    sizePresets,
    sizeLabel,
    type LabelKind,
    type LabelStyle,
  } from "../../lib/labels";
  import { attempt, type Schemas } from "../../lib/types";

  let { onBack }: { onBack: () => void } = $props();

  let details = $state<Schemas["NurseryDetails"]>({
    name: null,
    address: null,
    phone: null,
    email: null,
    website: null,
    registrationNumber: null,
    vatNumber: null,
    hasLogo: false,
    vatRegistered: false,
    paymentTermsDays: 30,
  });
  let styles = $state<Record<string, LabelStyle>>({});
  let kind = $state<LabelKind>("PlantLabel");
  let logoVersion = $state(0);
  let problem = $state("");
  let saved = $state("");
  let preview = $state("");
  let previewing = $state(false);
  let ownSize = $state(false);

  const style = $derived(styles[kind]);
  const about = $derived(kindsOfLabel.find((item) => item.kind === kind)!);
  const setProblem = (message: string) => (problem = message);

  async function load() {
    const { data } = await api.GET("/api/settings/nursery");
    details = data ?? details;
    styles = ((await api.GET("/api/settings/labels")).data ?? {}) as Record<
      string,
      LabelStyle
    >;
  }

  onMount(() => void attempt(load, setProblem));

  // Redraws the sample label a moment after a change, so the owner sees it before saving.
  let timer: ReturnType<typeof setTimeout> | undefined;
  $effect(() => {
    const chosen = $state.snapshot(style);
    const which = kind;
    void logoVersion;

    if (!chosen) {
      return;
    }

    clearTimeout(timer);
    timer = setTimeout(() => void draw(which, chosen as LabelStyle), 500);
  });

  async function draw(which: LabelKind, chosen: LabelStyle) {
    previewing = true;

    try {
      const response = await fetch(`/api/labels/preview/${which}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(chosen),
      });

      if (response.ok) {
        URL.revokeObjectURL(preview);
        preview = URL.createObjectURL(await response.blob());
      }
    } finally {
      previewing = false;
    }
  }

  const saveDetails = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.PUT("/api/settings/nursery", {
        body: details,
      });
      saved = response.ok ? "Nursery details saved." : "";
      problem = response.ok ? "" : "Those details couldn't be saved.";
    }, setProblem);
  };

  const chooseLogo = (event: Event) => {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    return attempt(async () => {
      const response = await fetch("/api/settings/nursery/logo", {
        method: "PUT",
        body: file,
      });
      input.value = "";
      problem = response.ok
        ? ""
        : "The logo must be a PNG or JPEG picture under 2MB.";
      logoVersion += 1;
      await load();
    }, setProblem);
  };

  const removeLogo = () =>
    attempt(async () => {
      await api.DELETE("/api/settings/nursery/logo");
      logoVersion += 1;
      await load();
    }, setProblem);

  const saveStyle = () =>
    attempt(async () => {
      const { response } = await api.PUT("/api/settings/labels/{kind}", {
        params: { path: { kind } },
        body: style,
      });
      saved = response.ok ? `${about.label} saved. New labels use it now.` : "";
      problem = response.ok ? "" : `Sizes must be between 20 and 300mm.`;
    }, setProblem);

  function choosePreset(value: string) {
    if (value === "own") {
      ownSize = true;
      return;
    }

    const preset = sizePresets.find((item) => sizeLabel(item) === value);

    if (preset) {
      ownSize = false;
      styles[kind] = {
        ...style,
        widthMm: preset.width,
        heightMm: preset.height,
      };
    }
  }
</script>

<main>
  <h1>Nursery and labels</h1>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if saved}<p class="card done" role="status">{saved}</p>{/if}

  <div class="split">
    <section>
      <form class="form-card stack" onsubmit={saveDetails}>
        <h2 class="form-heading">The nursery</h2>
        <label>Name <input bind:value={details.name} /></label>
        <label
          >Address <textarea bind:value={details.address} rows="3"
          ></textarea></label
        >
        <div class="field-grid">
          <label
            >Phone <input bind:value={details.phone} inputmode="tel" /></label
          >
          <label>Email <input bind:value={details.email} type="email" /></label>
        </div>
        <label>Website <input bind:value={details.website} /></label>
        <div class="field-grid">
          <label
            >Plant health registration <input
              bind:value={details.registrationNumber}
            /></label
          >
          <label>VAT number <input bind:value={details.vatNumber} /></label>
        </div>
        <label class="tick">
          <input type="checkbox" bind:checked={details.vatRegistered} />
          Registered for VAT, so invoices charge it
        </label>
        <label
          >Days customers have to pay <input
            type="number"
            min="0"
            max="365"
            bind:value={details.paymentTermsDays}
          /></label
        >
        <button type="submit">Save details</button>
      </form>

      <div class="form-card stack">
        <h2 class="form-heading">Logo</h2>
        {#if details.hasLogo}
          {#key logoVersion}
            <img
              class="logo-preview"
              src="/api/settings/nursery/logo?v={logoVersion}"
              alt="The nursery's logo"
            />
          {/key}
        {/if}
        <p class="muted">
          A one-colour logo prints best on a thermal label printer.
        </p>
        <label class="photo-choice">
          <ImageUp size={20} />
          <span>{details.hasLogo ? "Change the logo" : "Add a logo"}</span>
          <input
            type="file"
            accept="image/png,image/jpeg"
            onchange={chooseLogo}
          />
        </label>
        {#if details.hasLogo}
          <button class="link-button" onclick={removeLogo}
            >Remove the logo</button
          >
        {/if}
      </div>
    </section>

    <section class="stack">
      <div class="segmented" role="group" aria-label="Which label">
        {#each kindsOfLabel as item (item.kind)}
          <button
            aria-pressed={kind === item.kind}
            onclick={() => (kind = item.kind)}>{item.label}</button
          >
        {/each}
      </div>

      {#if style}
        <div class="form-card stack">
          <h2 class="form-heading">{about.label}</h2>
          <p class="muted">{about.use}</p>

          <div class="segmented" role="group" aria-label="Text size">
            {#each ["Compact", "Standard", "Large"] as layout (layout)}
              <button
                aria-pressed={style.layout === layout}
                onclick={() =>
                  (styles[kind] = {
                    ...style,
                    layout: layout as LabelStyle["layout"],
                  })}>{layout}</button
              >
            {/each}
          </div>

          <label>
            Size
            <select
              value={ownSize ||
              !sizePresets.some(
                (item) =>
                  item.width === Number(style.widthMm) &&
                  item.height === Number(style.heightMm),
              )
                ? "own"
                : sizeLabel({
                    width: Number(style.widthMm),
                    height: Number(style.heightMm),
                  })}
              onchange={(event) => choosePreset(event.currentTarget.value)}
            >
              {#each sizePresets as preset (sizeLabel(preset))}
                <option value={sizeLabel(preset)}>{sizeLabel(preset)}</option>
              {/each}
              <option value="own">Your own size…</option>
            </select>
          </label>
          <div class="field-grid">
            <label
              >Width (mm) <input
                type="number"
                min="20"
                max="300"
                value={style.widthMm}
                onchange={(event) =>
                  (styles[kind] = {
                    ...style,
                    widthMm: Number(event.currentTarget.value),
                  })}
              /></label
            >
            <label
              >Height (mm) <input
                type="number"
                min="20"
                max="300"
                value={style.heightMm}
                onchange={(event) =>
                  (styles[kind] = {
                    ...style,
                    heightMm: Number(event.currentTarget.value),
                  })}
              /></label
            >
          </div>

          <div class="tick-row">
            {#each about.switches as item (item.key)}
              <label class="tick">
                <input
                  type="checkbox"
                  checked={style[item.key]}
                  onchange={(event) =>
                    (styles[kind] = {
                      ...style,
                      [item.key]: event.currentTarget.checked,
                    })}
                />
                {item.label}
              </label>
            {/each}
          </div>
          {#if style.showLogo && !details.hasLogo}
            <p class="muted">Add a logo on the left for it to show.</p>
          {/if}

          <div class="form-actions">
            <button onclick={saveStyle}>Save {about.label.toLowerCase()}</button
            >
            {#if preview}
              <a
                class="button secondary"
                href={preview}
                target="_blank"
                rel="noopener">Print a test label</a
              >
            {/if}
          </div>
        </div>

        <div class="label-preview" aria-busy={previewing}>
          {#if preview}
            <iframe
              title="Preview of the {about.label.toLowerCase()}"
              src={preview}
            ></iframe>
          {:else}
            <p class="muted">Drawing a sample…</p>
          {/if}
        </div>
      {/if}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
