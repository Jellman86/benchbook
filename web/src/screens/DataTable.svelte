<script lang="ts" generics="T">
  import type { Snippet } from "svelte";
  import { cellText, sortRows, type Column } from "../lib/table";

  // A table for wide screens, sorted by tapping a column's heading.
  let {
    rows,
    columns,
    key,
    caption,
    onSelect,
    actions,
  }: {
    rows: T[];
    columns: Column<T>[];
    key: (row: T) => string;
    caption: string;
    onSelect?: (row: T) => void;
    actions?: Snippet<[T]>;
  } = $props();

  let sortBy = $state<number | null>(null);
  let ascending = $state(true);

  const sorted = $derived(
    sortRows(rows, sortBy === null ? null : columns[sortBy], ascending),
  );

  function sortOn(index: number) {
    ascending = sortBy === index ? !ascending : !columns[index].numeric;
    sortBy = index;
  }

  const sortState = (index: number) =>
    sortBy !== index ? "none" : ascending ? "ascending" : "descending";
</script>

<div class="table-wrap">
  <table class="data-table">
    <caption class="visually-hidden">{caption}</caption>
    <thead>
      <tr>
        {#each columns as column, index (column.label)}
          <th
            scope="col"
            aria-sort={sortState(index)}
            class:numeric={column.numeric}
          >
            <button class="sort-button" onclick={() => sortOn(index)}>
              {column.label}
              <span class="sort-mark" aria-hidden="true"
                >{sortBy === index ? (ascending ? "▲" : "▼") : ""}</span
              >
            </button>
          </th>
        {/each}
        {#if actions}<th scope="col"
            ><span class="visually-hidden">Actions</span></th
          >{/if}
      </tr>
    </thead>
    <tbody>
      {#each sorted as row (key(row))}
        <tr>
          {#each columns as column, index (column.label)}
            <td class:numeric={column.numeric}>
              {#if index === 0 && onSelect}
                <button class="link-button" onclick={() => onSelect(row)}
                  >{cellText(column, row)}</button
                >
              {:else}
                {cellText(column, row)}
              {/if}
            </td>
          {/each}
          {#if actions}<td class="row-actions">{@render actions(row)}</td>{/if}
        </tr>
      {/each}
    </tbody>
  </table>
</div>
