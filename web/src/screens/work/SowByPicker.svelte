<script lang="ts">
  import { monthEnd, sowByParts } from "../../lib/sowBy";

  // A seed packet's sow-by or use-by date, as packets give it: a month and a year.
  let { value = $bindable() }: { value: string } = $props();

  const months = [
    "January",
    "February",
    "March",
    "April",
    "May",
    "June",
    "July",
    "August",
    "September",
    "October",
    "November",
    "December",
  ];
  const thisYear = new Date().getFullYear();
  const years = Array.from({ length: 9 }, (_, index) => thisYear - 3 + index);

  const parts = $derived(sowByParts(value));

  function set(month: number | null, year: number | null) {
    value = month && year ? monthEnd(year, month) : "";
  }
</script>

<fieldset class="day-picker">
  <legend class="field-label">Sow by or use by, from the packet</legend>
  <div class="field-grid">
    <label
      >Month <select
        value={parts?.month ?? ""}
        onchange={(event) =>
          set(
            Number(event.currentTarget.value) || null,
            parts?.year ?? thisYear,
          )}
      >
        <option value="">No date</option>
        {#each months as name, index (name)}
          <option value={index + 1}>{name}</option>
        {/each}
      </select></label
    >
    <label
      >Year <select
        value={parts?.year ?? ""}
        disabled={!parts}
        onchange={(event) =>
          set(parts?.month ?? null, Number(event.currentTarget.value))}
      >
        {#if !parts}<option value="">—</option>{/if}
        {#each years as year (year)}
          <option value={year}>{year}</option>
        {/each}
      </select></label
    >
  </div>
</fieldset>
