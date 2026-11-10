<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import {
    kindsOfLabel,
    largestSize,
    sizeAdvice,
    sizeLabel,
    smallestSize,
    textSizes,
    type LabelKind,
    type LabelSize,
    type LabelStyle,
  } from "../../lib/labels";
  import { attempt, type Schemas } from "../../lib/types";

  let {
    onBack,
    onNurseryDetails,
    onPrinters,
  }: {
    onBack: () => void;
    onNurseryDetails: () => void;
    onPrinters: () => void;
  } = $props();

  // Only what the hints below need: whether there's a logo and a name to print.
  let details = $state<Schemas["NurseryDetails"] | null>(null);
  let styles = $state<Record<string, LabelStyle>>({});
  let kept = $state<Record<string, LabelStyle>>({});
  let kind = $state<LabelKind>("PlantLabel");
  let problem = $state("");
  let saved = $state("");
  let picture = $state("");
  let testLabel = $state("");
  let previewing = $state(false);
  let ownSize = $state<Record<string, boolean>>({});

  const style = $derived(styles[kind]);
  const about = $derived(kindsOfLabel.find((item) => item.kind === kind)!);
  const size = $derived<LabelSize>({
    width: Number(style?.widthMm),
    height: Number(style?.heightMm),
  });
  const advice = $derived(sizeAdvice(size));
  const isPreset = $derived(
    about.sizes.some(
      (item) => item.width === size.width && item.height === size.height,
    ),
  );
  const custom = $derived(ownSize[kind] || !isPreset);
  const changed = $derived(
    JSON.stringify(style) !== JSON.stringify(kept[kind]),
  );
  const setProblem = (message: string) => (problem = message);

  async function load() {
    const { data } = await api.GET("/api/settings/nursery");
    details = data ?? details;
  }

  async function loadStyles() {
    const { data } = await api.GET("/api/settings/labels");
    styles = (data ?? {}) as Record<string, LabelStyle>;
    kept = structuredClone($state.snapshot(styles));
  }

  onMount(
    () =>
      void attempt(async () => {
        await load();
        await loadStyles();
      }, setProblem),
  );

  // Redraws the sample label a moment after a change, so the owner sees it before saving.
  let timer: ReturnType<typeof setTimeout> | undefined;
  $effect(() => {
    const chosen = $state.snapshot(style);
    const which = kind;

    clearTimeout(timer);

    if (!chosen || advice.problem) {
      return;
    }

    timer = setTimeout(() => void draw(which, chosen as LabelStyle), 400);
  });

  // The sample comes twice: a picture every phone can show in the page, and the PDF to print.
  let drawing = 0;
  async function draw(which: LabelKind, chosen: LabelStyle) {
    const mine = ++drawing;
    previewing = true;

    const sample = (query: string) =>
      fetch(`/api/labels/preview/${which}${query}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(chosen),
      });

    try {
      const [shown, printable] = await Promise.all([
        sample("?picture=true"),
        sample(""),
      ]);

      // A newer change has been asked for since, so this sample is already out of date.
      if (mine !== drawing || !shown.ok || !printable.ok) {
        return;
      }

      URL.revokeObjectURL(picture);
      URL.revokeObjectURL(testLabel);
      picture = URL.createObjectURL(await shown.blob());
      testLabel = URL.createObjectURL(await printable.blob());
    } finally {
      if (mine === drawing) {
        previewing = false;
      }
    }
  }

  const saveStyle = () =>
    attempt(async () => {
      const { response } = await api.PUT("/api/settings/labels/{kind}", {
        params: { path: { kind } },
        body: style,
      });
      saved = response.ok ? `${about.label} saved. New labels use it now.` : "";
      problem = response.ok
        ? ""
        : `Sizes must be between ${smallestSize} and ${largestSize} mm.`;

      if (response.ok) {
        kept[kind] = structuredClone($state.snapshot(style)) as LabelStyle;
      }
    }, setProblem);

  function choosePreset(value: string) {
    if (value === "own") {
      ownSize[kind] = true;
      return;
    }

    const preset = about.sizes.find((item) => sizeLabel(item) === value);

    if (preset) {
      ownSize[kind] = false;
      styles[kind] = {
        ...style,
        widthMm: preset.width,
        heightMm: preset.height,
      };
    }
  }

  // Shown enlarged where there's room, but never more than three times life size.
  const pictureWidth = $derived(
    `min(100%, ${size.width * 3}mm, ${(24 * size.width) / size.height}rem)`,
  );
</script>

<main>
  <h1>Labels</h1>
  <p class="muted">
    How each kind of label looks. A change applies to every label printed after
    you save it. Which printer prints each kind is under <button
      class="link-button"
      onclick={onPrinters}>Printers</button
    >.
  </p>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  {#if saved}<p class="card done" role="status">{saved}</p>{/if}

  <section class="stack">
    <div class="segmented four" role="group" aria-label="Which label">
      {#each kindsOfLabel as item (item.kind)}
        <button
          class:secondary={kind !== item.kind}
          aria-pressed={kind === item.kind}
          onclick={() => (kind = item.kind)}>{item.label}</button
        >
      {/each}
    </div>

    {#if style}
      <div class="label-editor">
        <div class="form-card stack">
          <h3 class="form-heading">{about.label}</h3>
          <p class="muted">{about.use}</p>

          <label>
            Label size
            <select
              value={custom ? "own" : sizeLabel(size)}
              onchange={(event) => choosePreset(event.currentTarget.value)}
            >
              {#each about.sizes as preset, index (sizeLabel(preset))}
                <option value={sizeLabel(preset)}
                  >{sizeLabel(preset)}{index === 0
                    ? " (suggested)"
                    : ""}</option
                >
              {/each}
              <option value="own">Another size…</option>
            </select>
          </label>
          {#if custom}
            <div class="field-grid">
              <label
                >Width (mm) <input
                  type="number"
                  inputmode="numeric"
                  min={smallestSize}
                  max={largestSize}
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
                  inputmode="numeric"
                  min={smallestSize}
                  max={largestSize}
                  value={style.heightMm}
                  onchange={(event) =>
                    (styles[kind] = {
                      ...style,
                      heightMm: Number(event.currentTarget.value),
                    })}
                /></label
              >
            </div>
            <p class="muted">
              Measure one label, not the backing paper. Width is across the
              roll.
            </p>
          {/if}
          {#if advice.problem}
            <p class="problem" role="alert">{advice.problem}</p>
          {:else if advice.warning}
            <p class="caution" role="status">{advice.warning}</p>
          {/if}

          <div class="stack tight">
            <p class="field-label" id="text-size">Text size</p>
            <div class="segmented" role="group" aria-labelledby="text-size">
              {#each textSizes as item (item.layout)}
                <button
                  class:secondary={style.layout !== item.layout}
                  aria-pressed={style.layout === item.layout}
                  onclick={() =>
                    (styles[kind] = { ...style, layout: item.layout })}
                  >{item.label}</button
                >
              {/each}
            </div>
            <p class="muted">
              A long name is printed smaller so that it always fits.
            </p>
          </div>

          <div class="stack tight">
            <p class="field-label" id="label-shows">Show</p>
            <div class="tick-row" role="group" aria-labelledby="label-shows">
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
            {#if details && style.showLogo && !details.hasLogo}
              <p class="muted">
                Add a logo under <button
                  class="link-button"
                  onclick={onNurseryDetails}>Nursery details</button
                > for it to show on the label.
              </p>
            {/if}
            {#if details && style.showNurseryName && !details.name}
              <p class="muted">
                Fill in the nursery's name under <button
                  class="link-button"
                  onclick={onNurseryDetails}>Nursery details</button
                > for it to show on the label.
              </p>
            {/if}
          </div>

          <div class="form-actions">
            <button onclick={saveStyle} disabled={!!advice.problem}
              >Save {about.label.toLowerCase()}</button
            >
            {#if testLabel && !advice.problem}
              <a
                class="button secondary"
                href={testLabel}
                target="_blank"
                rel="noopener">Print a test label</a
              >
            {/if}
          </div>
          {#if changed}
            <p class="muted" role="status">
              Not saved yet. Labels print the old way until you save.
            </p>
          {/if}
        </div>

        <figure class="label-preview" aria-busy={previewing}>
          {#if picture && !advice.problem}
            <img
              src={picture}
              alt="A sample {about.label.toLowerCase()} as it will print"
              style:width={pictureWidth}
            />
            <figcaption class="muted">
              A sample {about.label.toLowerCase()}, {sizeLabel(size)}. It is
              shown larger than it prints.
            </figcaption>
          {:else if advice.problem}
            <p class="muted">Choose a size to see a sample.</p>
          {:else}
            <p class="muted">Drawing a sample…</p>
          {/if}
        </figure>
      </div>

      <details class="form-card printing-tips">
        <summary>Printing through the print window</summary>
        <ol>
          <li>
            <strong>Buy plastic labels and a resin ribbon.</strong>
            Polypropylene or polyester labels printed with a resin ribbon stand up
            to water, soil and sun. Paper labels, and plain thermal labels printed
            without a ribbon, fade and peel.
          </li>
          <li>
            <strong>Tell the printer the label's size.</strong> In the printer's
            own settings on the computer, add a label size of exactly
            {sizeLabel(size)} for this {about.label.toLowerCase()} and choose it when
            you print. Set it to labels with gaps and to thermal transfer.
          </li>
          <li>
            <strong>Print at actual size.</strong> In the print window choose 100%
            or "Actual size", never "Fit to page", with margins set to none and headers
            and footers off.
          </li>
          <li>
            <strong>Let the printer measure a new roll.</strong> After changing rolls,
            run the printer's calibration so that it finds the gaps between labels.
          </li>
          <li>
            <strong>Print one test label first.</strong> If the print sits off centre
            or runs over the edge, the size in the printer's settings doesn't match
            the roll.
          </li>
        </ol>
        <p class="muted">
          A desktop label printer prints about 104 mm wide at most, so 100 mm
          rolls are the widest worth buying. A plant passport is printed as its
          own label, so these designs leave it out.
        </p>
      </details>
    {/if}
  </section>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
