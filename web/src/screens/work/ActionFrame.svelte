<script lang="ts">
  import type { Snippet } from "svelte";
  import type { Schemas } from "../../lib/types";
  import { lineAmount } from "../../lib/sowing";
  import { plantName } from "../../lib/work";

  let {
    title,
    problem,
    onCancel,
    container = null,
    children,
  }: {
    title: string;
    problem: string;
    onCancel: () => void;
    container?: Schemas["ContainerResponse"] | null;
    children: Snippet;
  } = $props();
</script>

<main class="action-frame">
  <h1>{title}</h1>
  {#if container}
    <!-- After an interruption, this says which tray the action is for. -->
    <p class="action-for">
      <strong>{container.labelCode}</strong>
      {container.contents
        .map(
          (line) =>
            `${lineAmount(line)} ${plantName(line.commonName, line.variety)}`,
        )
        .join(", ")}{container.locationName
        ? ` · ${container.locationName}`
        : ""}
    </p>
  {/if}
  {@render children()}
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  <button class="back" onclick={onCancel}>Back</button>
</main>
