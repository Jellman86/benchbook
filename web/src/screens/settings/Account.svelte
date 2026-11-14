<script lang="ts">
  import { isOn } from "../../lib/features.svelte";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import type { Me } from "../../lib/boot";
  import { createPasskey, passkeyProblem } from "../../lib/passkey";
  import { themes, type Theme } from "../../lib/theme";
  import { playTune, stopTune, unlockTune } from "../../lib/tune";
  import { attempt, type Schemas } from "../../lib/types";

  let {
    me,
    theme,
    onTheme,
    onBack,
  }: {
    me: Me;
    theme: Theme;
    onTheme: (theme: Theme) => void;
    onBack: () => void;
  } = $props();

  let settings = $state<Schemas["AccountSettings"] | null>(null);
  let currentPassword = $state("");
  let newPassword = $state("");
  let pin = $state("");
  let problem = $state("");
  let saved = $state("");

  const setProblem = (message: string) => (problem = message);

  async function load() {
    settings = (await api.GET("/api/account/settings")).data ?? null;
  }

  onMount(() => void attempt(load, setProblem));

  function report(ok: boolean, success: string, failure: string) {
    problem = ok ? "" : failure;
    saved = ok ? success : "";
  }

  const savePassword = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/account/settings/password", {
        body: {
          currentPassword: settings?.hasPassword ? currentPassword : null,
          newPassword,
        },
      });

      report(
        response.ok,
        "Password saved.",
        response.status === 403
          ? "Your current password isn't right."
          : "A password must be at least 8 characters.",
      );

      if (response.ok) {
        currentPassword = "";
        newPassword = "";
        await load();
      }
    }, setProblem);
  };

  const savePin = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/account/settings/pin", {
        body: { pin },
      });

      report(response.ok, "PIN saved.", "A PIN must be 4 to 6 digits.");

      if (response.ok) {
        pin = "";
        await load();
      }
    }, setProblem);
  };

  const switchTune = (event: Event) => {
    const on = (event.currentTarget as HTMLInputElement).checked;

    // Still inside the tap, so the tune may play once it's switched on.
    unlockTune();

    return attempt(async () => {
      await api.PUT("/api/account/tune/on", { body: { on } });

      if (on) {
        playTune();
      } else {
        stopTune();
      }

      await load();
    }, setProblem);
  };

  const addPasskey = () =>
    attempt(async () => {
      const options = await api.POST("/api/account/settings/passkey-options", {
        parseAs: "text",
      });
      let credential;

      try {
        credential = await createPasskey(options.data as unknown as string);
      } catch (error) {
        report(false, "", passkeyProblem(error));
        return;
      }

      const { response } = await api.POST("/api/account/settings/passkey", {
        body: { credential: credential as never },
      });

      report(
        response.ok,
        "Passkey saved on this device.",
        "That didn't finish. Please try again.",
      );
      await load();
    }, setProblem);
</script>

<main>
  <h1>My account</h1>
  <p class="muted">Signed in as {me.name}.</p>

  <div class="stack narrow">
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    {#if saved}<p class="muted" role="status">{saved}</p>{/if}

    <h2>Appearance</h2>
    <div class="segmented">
      {#each themes as item (item.theme)}
        <button
          class={theme === item.theme ? "" : "secondary"}
          aria-pressed={theme === item.theme}
          onclick={() => onTheme(item.theme)}>{item.label}</button
        >
      {/each}
    </div>

    {#if settings?.hasTune && !me.isTeam}
      <h2>Sign-in tune</h2>
      <label class="tick">
        <input
          type="checkbox"
          checked={settings.tuneOn}
          onchange={switchTune}
        />
        Play my tune when I sign in
      </label>
      <p class="muted">
        {me.isOwner
          ? "You can change it under Staff."
          : "The owner chose it for you."}
      </p>
    {/if}

    {#if me.isTeam}
      <p class="muted">
        This device is signed in as the nursery team. To use your own account,
        sign in as yourself.
      </p>
    {:else if settings}
      <h2>Signing in</h2>
      {#if settings.nameOnly}
        <p class="muted">
          You sign in by tapping your name. A password or passkey lets you sign
          in on other devices too.
        </p>
      {/if}

      <form class="stack" onsubmit={savePassword}>
        {#if settings.hasPassword}
          <label>
            Current password
            <input
              type="password"
              bind:value={currentPassword}
              autocomplete="current-password"
              required
            />
          </label>
        {/if}
        <label>
          New password (at least 8 characters)
          <input
            type="password"
            bind:value={newPassword}
            autocomplete="new-password"
            required
          />
        </label>
        <button type="submit" class="secondary"
          >{settings.hasPassword ? "Change password" : "Set a password"}</button
        >
      </form>

      {#if settings.externalSignInName}
        {#if settings.hasExternalLogin}
          <p class="muted">
            Connected to your {settings.externalSignInName} login.
          </p>
        {:else}
          <a class="button secondary" href="/api/account/external/link"
            >Connect my {settings.externalSignInName} login</a
          >
        {/if}
      {/if}

      <button class="secondary" onclick={addPasskey}
        >{settings.hasPasskey
          ? "Add a passkey on this device too"
          : "Add a passkey on this device"}</button
      >

      {#if isOn("BenchStations")}
        <form class="stack" onsubmit={savePin}>
          <label>
            {settings.hasPin ? "New bench PIN" : "Bench PIN"} (4 to 6 digits)
            <input
              bind:value={pin}
              inputmode="numeric"
              autocomplete="off"
              required
            />
          </label>
          <button type="submit" class="secondary">Save PIN</button>
        </form>
      {/if}
    {/if}
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
