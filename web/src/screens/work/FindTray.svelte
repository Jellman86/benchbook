<script lang="ts">
  import { api } from "../../api/client";
  import { findTrays } from "../../lib/findTray";
  import { rememberContainers, rememberedContainers } from "../../lib/offline";
  import { stageLabel } from "../../lib/production";
  import type { Schemas } from "../../lib/types";
  import { plantName } from "../../lib/work";

  // For trays with no label yet, or whose code nobody knows: find them by what's in them or where they are.
  let { onOpen }: { onOpen: (label: string) => void } = $props();

  let query = $state("");
  let containers = $state<Schemas["ContainerResponse"][]>(
    rememberedContainers(),
  );
  let fetched = false;

  const found = $derived(findTrays(containers, query));

  // Fresh from the server when someone starts looking; what the device remembers otherwise.
  async function refresh() {
    if (fetched) {
      return;
    }

    fetched = true;

    try {
      const { data } = await api.GET("/api/containers/active");

      if (data) {
        containers = data;
        rememberContainers(data);
      }
    } catch {
      // No signal: the remembered trays will do.
    }
  }

  const contents = (container: Schemas["ContainerResponse"]) =>
    container.contents
      .map(
        (line) =>
          `${line.quantity} ${plantName(line.commonName, line.variety)}`,
      )
      .join(", ") || "Empty";
</script>

<div class="find-tray stack tight">
  <label>
    No label? Find a tray
    <input
      type="search"
      bind:value={query}
      onfocus={refresh}
      autocomplete="off"
      placeholder="Plant, place or code"
    />
  </label>

  {#if query.trim()}
    {#if found.length === 0}
      <p class="muted">No tray with plants matches that.</p>
    {:else}
      <ul class="place-tree" aria-label="Trays found">
        {#each found as container (container.id)}
          <li>
            <button
              class="place-row"
              onclick={() => onOpen(container.labelCode)}
            >
              <span class="plant-names">
                <strong>{contents(container)}</strong>
                <em
                  >{container.labelCode} · {container.locationName ??
                    "No place recorded"}{container.stage
                    ? ` · ${stageLabel(container.stage)}`
                    : ""}</em
                >
              </span>
            </button>
          </li>
        {/each}
      </ul>
    {/if}
  {/if}
</div>
