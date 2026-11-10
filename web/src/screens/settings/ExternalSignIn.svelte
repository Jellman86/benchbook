<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, optional, type Schemas } from "../../lib/types";

  // Single sign-on through the nursery's OpenID Connect provider, such as Authentik.
  const problems: Record<string, string> = {
    incomplete: "Enter an https address, a client ID and a client secret.",
    unreachable: "GrowLog couldn't reach that provider. Check the address.",
    "wrong-issuer":
      "That address answered, but as a different provider. Use the exact issuer address.",
    "last-way-in":
      "Give an owner a password or passkey first. Otherwise nobody with owner rights could get back in.",
  };

  let settings = $state<Schemas["ExternalSignInSettings"] | null>(null);
  let enabled = $state(false);
  let authority = $state("");
  let clientId = $state("");
  let clientSecret = $state("");
  let displayName = $state("");
  let ownerGroup = $state("");
  let problem = $state("");
  let saved = $state("");
  let busy = $state(false);

  const setProblem = (message: string) => (problem = message);

  async function load() {
    settings = (await api.GET("/api/external-sign-in")).data ?? null;

    if (settings) {
      enabled = settings.enabled;
      authority = settings.authority ?? "";
      clientId = settings.clientId ?? "";
      displayName = settings.displayName;
      ownerGroup = settings.ownerGroup ?? "";
      clientSecret = "";
    }
  }

  onMount(() => void attempt(load, setProblem));

  const changingProvider = () =>
    settings?.enabled &&
    (!enabled ||
      authority.trim().replace(/\/$/, "") !==
        settings.authority?.replace(/\/$/, ""));

  const save = (event: SubmitEvent) => {
    event.preventDefault();

    if (
      changingProvider() &&
      !confirm(
        "Everyone will need to connect their staff login again, and anyone who only signs in that way may lose access. Carry on?",
      )
    ) {
      return;
    }

    return attempt(async () => {
      busy = true;
      saved = "";
      problem = "";

      try {
        const { error, response } = await api.PUT("/api/external-sign-in", {
          body: {
            enabled,
            authority: optional(authority),
            clientId: optional(clientId),
            clientSecret: optional(clientSecret),
            displayName: optional(displayName),
            ownerGroup: optional(ownerGroup),
          },
        });

        if (response.ok) {
          saved = "Saved. This applies straight away.";
          await load();
        } else {
          problem =
            problems[error?.problem ?? ""] ??
            "That didn't save. Please try again.";
        }
      } finally {
        busy = false;
      }
    }, setProblem);
  };
</script>

<section class="form-card stack">
  <h2 class="form-heading">Staff login</h2>
  <p class="muted">
    Lets staff sign in with one shared login from an OpenID Connect provider,
    such as Authentik. GrowLog's own passkeys, passwords and bench PINs keep
    working alongside it.
  </p>

  {#if settings}
    <form class="stack" onsubmit={save}>
      <label class="tick">
        <input type="checkbox" bind:checked={enabled} /> Offer this on the sign-in
        page
      </label>
      <label>
        Provider address
        <input
          bind:value={authority}
          inputmode="url"
          autocomplete="off"
          placeholder="https://auth.example.com/application/o/growlog/"
        />
      </label>
      <label>
        Client ID
        <input bind:value={clientId} autocomplete="off" />
      </label>
      <label>
        Client secret
        <input
          type="password"
          bind:value={clientSecret}
          autocomplete="new-password"
          placeholder={settings.hasClientSecret
            ? "Saved. Leave blank to keep it."
            : ""}
        />
      </label>
      <label>
        Name on the button
        <input bind:value={displayName} autocomplete="off" />
      </label>
      <label>
        Group that gets owner rights (optional)
        <input bind:value={ownerGroup} autocomplete="off" />
      </label>

      <div class="card">
        <strong>Redirect address</strong>
        <p class="muted">Add this to the application at your provider.</p>
        <p class="link" data-testid="redirect-uri">{settings.redirectUri}</p>
      </div>

      {#if settings.fromDeployment}
        <p class="muted">
          These come from the server's settings until you save here.
        </p>
      {/if}
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
      {#if saved}<p class="muted" role="status">{saved}</p>{/if}

      <button type="submit" disabled={busy}>Save</button>
    </form>
  {:else if problem}
    <p class="problem" role="alert">{problem}</p>
  {/if}
</section>
