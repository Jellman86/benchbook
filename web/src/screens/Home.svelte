<script lang="ts">
  import { api } from "../api/client";
  import type { Me } from "../lib/boot";

  let { me, onSignedOut }: { me: Me; onSignedOut: () => void } = $props();

  async function signOut() {
    await api.POST("/api/account/sign-out");
    onSignedOut();
  }
</script>

<main>
  <h1>Hello, {me.name}</h1>
  <p class="muted">Recording work is coming next.</p>
  <button class="secondary" onclick={signOut}>Finished</button>
</main>
