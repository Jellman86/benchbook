<script lang="ts">
  import type { Snippet } from "svelte";

  // A package's one plant passport, drawn the way it prints: part B once, then each batch's botanical name (A),
  // code (C) and origin (D) on one row, so every code and origin stays with its plant.
  let {
    partB,
    lines,
    caption,
    children,
  }: {
    partB: string;
    lines: { partA: string; partC: string; partD: string }[];
    caption: string;
    children?: Snippet;
  } = $props();
</script>

<figure class="passport package-passport">
  <figcaption class="visually-hidden">{caption}</figcaption>
  <p class="passport-title" aria-hidden="true">UK Plant Passport</p>
  <dl>
    <dt>B</dt>
    <dd>{partB}</dd>
  </dl>
  <table>
    <thead>
      <tr>
        <th scope="col"><abbr title="Botanical name">A</abbr></th>
        <th scope="col"><abbr title="Traceability code">C</abbr></th>
        <th scope="col"><abbr title="Origin">D</abbr></th>
      </tr>
    </thead>
    <tbody>
      {#each lines as line (line.partC + line.partA + line.partD)}
        <tr>
          <td class="botanical">{line.partA}</td>
          <td>{line.partC}</td>
          <td>{line.partD}</td>
        </tr>
      {/each}
    </tbody>
  </table>
  {#if children}
    <div class="passport-foot">{@render children()}</div>
  {/if}
</figure>
