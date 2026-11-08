<script lang="ts">
  import { api } from "../api/client";
  import type { Me } from "../lib/boot";
  import Manage from "./Manage.svelte";

  let { me, onSignedOut }: { me: Me; onSignedOut: () => void } = $props();

  let managing = $state(false);

  async function signOut() {
    await api.POST("/api/account/sign-out");
    onSignedOut();
  }
</script>

{#if managing}
  <Manage onBack={() => (managing = false)} onBecameStation={onSignedOut} />
{:else}
  <main>
    <h1>Hello, {me.name}</h1>
    <p class="muted">Recording work is coming next.</p>
    {#if me.isOwner}
      <button onclick={() => (managing = true)}>Manage</button>
    {/if}
    <button class="secondary" onclick={signOut}>Finished</button>
  </main>
{/if}
