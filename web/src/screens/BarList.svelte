<script lang="ts">
  // One series as horizontal bars, each labelled with its value at the tip.
  // The series picks the colour: 1 for how many plants, 2 for survival, 3 for losses.
  let {
    rows,
    caption,
    max,
    series = 1,
  }: {
    rows: { label: string; value: number; text?: string }[];
    caption: string;
    max?: number;
    series?: 1 | 2 | 3;
  } = $props();

  const largest = $derived(max ?? Math.max(1, ...rows.map((row) => row.value)));
</script>

<dl class="bar-list series-{series}" aria-label={caption}>
  {#each rows as row (row.label)}
    <dt>{row.label}</dt>
    <dd title="{row.label}: {row.text ?? row.value}">
      <span
        class="bar"
        style:width="{Math.max(0, (row.value / largest) * 100) * 0.8}%"
      ></span>
      {row.text ?? row.value}
    </dd>
  {/each}
</dl>
