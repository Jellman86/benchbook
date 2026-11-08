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
    presets?: number[];
  } = $props();

  function change(by: number) {
    value = Math.min(max, Math.max(min, value + by));
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
  {#if presets.length > 0}
    <div class="row">
      {#each presets.filter((preset) => preset <= max) as preset (preset)}
        <button type="button" class="secondary" onclick={() => (value = preset)}
          >{preset}</button
        >
      {/each}
    </div>
  {/if}
</div>
