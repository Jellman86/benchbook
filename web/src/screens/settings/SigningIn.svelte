<script lang="ts">
  import ExternalSignIn from "./ExternalSignIn.svelte";
  import SignInLevel from "./SignInLevel.svelte";

  let { onBack }: { onBack: () => void } = $props();
</script>

<main>
  <h1>Signing in</h1>
  <p class="muted">
    This is how staff get into Hortiplanner. Each person's password, PIN and
    invitation are under Staff.
  </p>

  <div class="split">
    <section>
      <SignInLevel />
    </section>
    <section>
      <ExternalSignIn />
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
