<script lang="ts">
  import LogOut from "@lucide/svelte/icons/log-out";
  import { onMount } from "svelte";
  import type { Me } from "../lib/boot";
  import { canOpen, settingsGroups, type SettingsPage } from "../lib/settings";
  import type { Theme } from "../lib/theme";
  import About from "./settings/About.svelte";
  import Account from "./settings/Account.svelte";
  import DataAndBackups from "./settings/DataAndBackups.svelte";
  import Labels from "./settings/Labels.svelte";
  import NurseryDetails from "./settings/NurseryDetails.svelte";
  import PotSizes from "./settings/PotSizes.svelte";
  import SigningIn from "./settings/SigningIn.svelte";
  import Staff from "./settings/Staff.svelte";
  import Stations from "./settings/Stations.svelte";

  let {
    me,
    page,
    theme,
    onPage,
    onTheme,
    onBack,
    onSignOut,
    onBecameStation,
  }: {
    me: Me;
    page: SettingsPage | null;
    theme: Theme;
    onPage: (page: SettingsPage | null) => void;
    onTheme: (theme: Theme) => void;
    onBack: () => void;
    onSignOut: () => void;
    onBecameStation: () => void;
  } = $props();

  // A wide screen keeps the list of settings beside the page; a phone shows one or the other.
  const wideQuery = "(min-width: 900px)";
  let wide = $state(matchMedia(wideQuery).matches);

  onMount(() => {
    const query = matchMedia(wideQuery);
    const changed = () => (wide = query.matches);
    query.addEventListener("change", changed);
    return () => query.removeEventListener("change", changed);
  });

  const groups = $derived(settingsGroups(me.isOwner));
  const allowed = $derived(page && canOpen(page, me.isOwner) ? page : null);
  const shown = $derived(allowed ?? (wide ? "account" : null));
  const toList = () => onPage(null);
  const signOutLabel = $derived(me.isTeam ? "Sign in as yourself" : "Sign out");
</script>

{#snippet list(asMenu: boolean)}
  {#each groups as group (group.heading)}
    {#if asMenu}
      <h2 class="settings-heading">{group.heading}</h2>
    {:else}
      <p class="settings-nav-heading">{group.heading}</p>
    {/if}
    {#each group.items as item (item.page)}
      <button
        class={asMenu ? "secondary menu-item" : ""}
        aria-current={!asMenu && shown === item.page ? "page" : undefined}
        onclick={() => onPage(item.page)}
      >
        <item.icon size={20} />
        {item.label}
      </button>
    {/each}
  {/each}
{/snippet}

{#if shown === null}
  <main>
    <h1>Settings</h1>
    <p class="muted">Signed in as {me.name}.</p>
    <div class="stack">
      {@render list(true)}
      <button class="secondary menu-item settings-sign-out" onclick={onSignOut}>
        <LogOut size={20} />
        {signOutLabel}
      </button>
    </div>
    <button class="back" onclick={onBack}>Back</button>
  </main>
{:else}
  <div class="settings-layout">
    {#if wide}
      <nav class="settings-nav" aria-label="Settings">
        <p class="settings-title">Settings</p>
        {@render list(false)}
      </nav>
    {/if}

    {#if shown === "account"}
      <Account {me} {theme} {onTheme} onBack={toList} />
    {:else if shown === "nursery"}
      <NurseryDetails onBack={toList} />
    {:else if shown === "labels"}
      <Labels onBack={toList} onNurseryDetails={() => onPage("nursery")} />
    {:else if shown === "potSizes"}
      <PotSizes onBack={toList} />
    {:else if shown === "staff"}
      <Staff onBack={toList} onSigningIn={() => onPage("signingIn")} />
    {:else if shown === "signingIn"}
      <SigningIn onBack={toList} />
    {:else if shown === "stations"}
      <Stations onBack={toList} {onBecameStation} />
    {:else if shown === "data"}
      <DataAndBackups onBack={toList} />
    {:else if shown === "about"}
      <About onBack={toList} />
    {/if}
  </div>
{/if}
