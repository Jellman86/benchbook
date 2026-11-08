<script lang="ts">
  import { onMount } from "svelte";
  import { api, sessionEnded } from "./api/client";
  import { chooseStart, readInvite, type Start } from "./lib/boot";
  import Bench from "./screens/Bench.svelte";
  import Home from "./screens/Home.svelte";
  import Invite from "./screens/Invite.svelte";
  import Setup from "./screens/Setup.svelte";
  import SignIn from "./screens/SignIn.svelte";

  let start = $state<Start | null>(null);
  let unreachable = $state(false);

  async function load(invite = readInvite(location.pathname, location.search)) {
    try {
      unreachable = false;
      start = await chooseStart(
        {
          me: async () => (await api.GET("/api/account/me")).data ?? null,
          benchPeople: async () =>
            (await api.GET("/api/bench/people")).data ?? null,
          needsSetup: async () =>
            (await api.GET("/api/setup/status")).data?.needsSetup ?? false,
        },
        invite,
      );
    } catch {
      unreachable = true;
    }
  }

  function finished() {
    history.replaceState(null, "", "/");
    void load(null);
  }

  function onSessionEnded() {
    if (start?.screen === "home") {
      void load(null);
    }
  }

  onMount(() => {
    void load();
    window.addEventListener(sessionEnded, onSessionEnded);
    return () => window.removeEventListener(sessionEnded, onSessionEnded);
  });
</script>

{#if unreachable}
  <main>
    <h1>GrowLog</h1>
    <p class="problem">
      Can't reach GrowLog. Check the connection and try again.
    </p>
    <button onclick={() => load()}>Try again</button>
  </main>
{:else if start?.screen === "home"}
  <Home me={start.me} onSignedOut={finished} />
{:else if start?.screen === "bench"}
  <Bench people={start.people} onSignedIn={finished} />
{:else if start?.screen === "setup"}
  <Setup onDone={finished} />
{:else if start?.screen === "signIn"}
  <SignIn onSignedIn={finished} />
{:else if start?.screen === "invite"}
  <Invite invite={start.invite} onDone={finished} />
{:else}
  <main><p class="muted">Loading…</p></main>
{/if}
