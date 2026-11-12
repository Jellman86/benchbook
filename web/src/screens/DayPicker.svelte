<script lang="ts">
  import CalendarDays from "@lucide/svelte/icons/calendar-days";
  import ChevronLeft from "@lucide/svelte/icons/chevron-left";
  import ChevronRight from "@lucide/svelte/icons/chevron-right";
  import {
    addDays,
    addMonths,
    dayWords,
    isoDay,
    isWithin,
    longDay,
    monthGrid,
    monthName,
  } from "../lib/days";

  // A day chosen with one tap for today or yesterday, or from a calendar for anything earlier.
  let {
    value = $bindable(),
    label,
    min = null,
    max = isoDay(new Date()),
  }: {
    value: string;
    label: string;
    min?: string | null;
    max?: string | null;
  } = $props();

  const now = new Date();
  const today = isoDay(now);
  const yesterday = addDays(today, -1);
  const weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

  let open = $state(false);
  let shown = $state(addMonths(today, 0));

  const quick = $derived(
    [
      { day: today, words: "Today" },
      { day: yesterday, words: "Yesterday" },
    ].filter((item) => isWithin(item.day, min, max)),
  );
  const other = $derived(!quick.some((item) => item.day === value));
  const grid = $derived(monthGrid(shown));
  const canGoBack = $derived(!min || addDays(shown, -1) >= min);
  const canGoOn = $derived(!max || addMonths(shown, 1) <= max);

  function toggle() {
    shown = addMonths(value || today, 0);
    open = !open;
  }

  function pick(day: string) {
    value = day;
    open = false;
  }
</script>

<fieldset class="day-picker">
  <legend class="field-label">{label}</legend>
  <div class="day-choices">
    {#each quick as item (item.day)}
      <button
        type="button"
        aria-pressed={value === item.day}
        onclick={() => pick(item.day)}>{item.words}</button
      >
    {/each}
    <button
      type="button"
      aria-pressed={other && !!value}
      aria-expanded={open}
      onclick={toggle}
      ><CalendarDays size={18} />
      {other && value ? dayWords(value, now) : "Another day"}</button
    >
  </div>

  {#if open}
    <div class="calendar">
      <div class="calendar-head">
        <button
          type="button"
          class="secondary"
          aria-label="Previous month"
          disabled={!canGoBack}
          onclick={() => (shown = addMonths(shown, -1))}
          ><ChevronLeft size={20} /></button
        >
        <strong aria-live="polite">{monthName(shown)}</strong>
        <button
          type="button"
          class="secondary"
          aria-label="Next month"
          disabled={!canGoOn}
          onclick={() => (shown = addMonths(shown, 1))}
          ><ChevronRight size={20} /></button
        >
      </div>
      <div class="calendar-grid">
        {#each weekdays as weekday (weekday)}
          <span class="calendar-weekday" aria-hidden="true"
            >{weekday.slice(0, 1)}</span
          >
        {/each}
        {#each grid as day, index (day ?? `blank-${index}`)}
          {#if day}
            <button
              type="button"
              aria-pressed={day === value}
              aria-current={day === today ? "date" : undefined}
              aria-label={longDay(day)}
              disabled={!isWithin(day, min, max)}
              onclick={() => pick(day)}>{Number(day.slice(8))}</button
            >
          {:else}
            <span></span>
          {/if}
        {/each}
      </div>
    </div>
  {/if}
</fieldset>
