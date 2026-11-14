<script lang="ts">
  import ExternalLink from "@lucide/svelte/icons/external-link";
  import type { Schemas } from "../../lib/types";

  let { title, article }: { title: string; article: Schemas["Credited"] } =
    $props();

  const shortEnough = 420;

  let open = $state(false);

  const long = $derived(article.text.length > shortEnough);
  const paragraphs = $derived(
    article.text
      .split(/\n+/u)
      .map((part) => part.trim())
      .filter(Boolean),
  );
  const credit = $derived(
    article.licence ? `${article.source}, ${article.licence}` : article.source,
  );
</script>

<section class="article-block appear">
  <h2>{title}</h2>
  <div class="article-text" class:clamped={long && !open}>
    {#each paragraphs as paragraph, index (index)}
      <p>{paragraph}</p>
    {/each}
  </div>
  {#if long}
    <button
      type="button"
      class="link-button"
      aria-expanded={open}
      onclick={() => (open = !open)}>{open ? "Show less" : "Read more"}</button
    >
  {/if}
  <p class="muted credit">
    From
    {#if article.url}
      <a
        class="small-button"
        href={article.url}
        target="_blank"
        rel="noopener noreferrer"
        >{credit} <ExternalLink size={16} aria-hidden="true" /></a
      >
    {:else}
      {credit}
    {/if}
  </p>
</section>
