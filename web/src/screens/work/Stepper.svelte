<script lang="ts">
  // Quantities without a keyboard, for wet hands and gloves.
  let {
    value = $bindable(),
    min = 1,
    max = 1_000_000,
    presets = [],
  }: {
    value: number;
    min?: number;
    max?: number;
    presets?: (number | { label: string; value: number })[];
  } = $props();

  let typing = $state(false);

  function change(by: number) {
    value = Math.min(max, Math.max(min, value + by));
  }

  function typed(event: Event) {
    const entered = Number((event.currentTarget as HTMLInputElement).value);

    if (Number.isFinite(entered)) {
      value = Math.min(max, Math.max(min, Math.round(entered)));
    }
  }
</script>

<div class="stepper">
  <p class="stepper-value" aria-live="polite" data-testid="quantity">{value}</p>
  <div class="stepper-buttons">
    <button type="button" class="secondary" onclick={() => change(-10)}
      >−10</button
    >
    <button type="button" class="secondary" onclick={() => change(-1)}
      >−1</button
    >
    <button type="button" class="secondary" onclick={() => change(1)}>+1</button
    >
    <button type="button" class="secondary" onclick={() => change(10)}
      >+10</button
    >
  </div>
  {#if typing}
    <label>
      How many
      <input
        type="number"
        inputmode="numeric"
        {min}
        {max}
        {value}
        onchange={typed}
      />
    </label>
  {:else}
    <button type="button" class="link-button" onclick={() => (typing = true)}
      >Type a number instead</button
    >
  {/if}
  {#if presets.length > 0}
    <div class="row">
      {#each presets
        .map( (preset) => (typeof preset === "number" ? { label: String(preset), value: preset } : preset) )
        .filter((preset) => preset.value <= max) as preset (preset.value)}
        <button
          type="button"
          class="secondary"
          onclick={() => (value = preset.value)}>{preset.label}</button
        >
      {/each}
    </div>
  {/if}
</div>
