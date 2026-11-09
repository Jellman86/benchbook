<script lang="ts">
  import type { Me } from "../lib/boot";
  import { themes, type Theme } from "../lib/theme";

  let {
    me,
    theme,
    onTheme,
    onFinished,
  }: {
    me: Me;
    theme: Theme;
    onTheme: (theme: Theme) => void;
    onFinished: () => void;
  } = $props();
</script>

<main>
  <h1>{me.name}</h1>

  <h2>Appearance</h2>
  <div class="segmented">
    {#each themes as item (item.theme)}
      <button
        class={theme === item.theme ? "" : "secondary"}
        aria-pressed={theme === item.theme}
        onclick={() => onTheme(item.theme)}>{item.label}</button
      >
    {/each}
  </div>

  <button class="secondary" onclick={onFinished}>Finished</button>
</main>
