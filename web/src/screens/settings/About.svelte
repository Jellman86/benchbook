<script lang="ts">
  import ExternalLink from "@lucide/svelte/icons/external-link";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { momentLabel } from "../../lib/dates";
  import { attempt, type Schemas } from "../../lib/types";

  // Hortiplanner is under the GNU AGPL: everyone using it over the network is offered the source of this version.
  let { onBack }: { onBack: () => void } = $props();

  const project = "https://github.com/Jellman86/hortiplanner";

  let about = $state<Schemas["About"] | null>(null);
  let problem = $state("");

  onMount(
    () =>
      void attempt(
        async () => {
          about = (await api.GET("/api/about")).data ?? null;
        },
        (message) => (problem = message),
      ),
  );

  const licence = $derived(`${project}/blob/${about?.commit ?? "dev"}/LICENSE`);

  const parts: { name: string; use: string; licence: string }[] = [
    { name: ".NET and ASP.NET Core", use: "the server", licence: "MIT" },
    { name: "PostgreSQL", use: "the database", licence: "PostgreSQL" },
    { name: "Npgsql", use: "talking to the database", licence: "PostgreSQL" },
    { name: "Svelte", use: "the screens", licence: "MIT" },
    { name: "Typst", use: "label and invoice PDFs", licence: "Apache 2.0" },
    {
      name: "QRCoder",
      use: "QR codes on labels and invitations",
      licence: "MIT",
    },
    { name: "jsQR", use: "scanning with the camera", licence: "Apache 2.0" },
    { name: "PdfPig", use: "reading supplier invoices", licence: "Apache 2.0" },
    { name: "Leaflet", use: "the places map", licence: "BSD 2-clause" },
    {
      name: "Leaflet-Geoman",
      use: "drawing places on the map",
      licence: "MIT",
    },
    { name: "Lucide", use: "icons", licence: "ISC" },
  ];
</script>

<main>
  <h1>About Hortiplanner</h1>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

  <div class="split">
    <section class="stack">
      <div class="about-intro">
        <img
          src="/logo-96.png"
          srcset="/logo-96.png 1x, /icon-192.png 2x"
          alt=""
          width="64"
          height="64"
        />
        <p>
          Simple, open-source record keeping for small plant nurseries. Scan a
          label, press what happened, and get on with the day.
        </p>
      </div>

      {#if about}
        <dl class="facts card">
          <dt>Version</dt>
          <dd>{about.version}</dd>
          {#if about.commit}
            <dt>Built from</dt>
            <dd>
              <a href={about.sourceUrl} target="_blank" rel="noopener"
                ><code>{about.commit.slice(0, 7)}</code></a
              >
            </dd>
          {/if}
          {#if about.builtAt}
            <dt>Built</dt>
            <dd>{momentLabel(about.builtAt)}</dd>
          {/if}
          <dt>Runs on</dt>
          <dd>{about.runtime}, {about.database}</dd>
        </dl>
      {:else if !problem}
        <p class="muted">Loading…</p>
      {/if}

      <div class="form-card stack">
        <h2 class="form-heading">Free software</h2>
        <p>
          Hortiplanner is free software. You can use, share and change it under
          the
          <a href={licence} target="_blank" rel="noopener"
            >GNU Affero General Public License</a
          >, version 3 or any later version. It comes with no warranty.
        </p>
        <p class="muted">
          Anyone using Hortiplanner is entitled to its source code, including
          any changes made to this copy. The source of this exact version is
          here.
        </p>
        <div class="row">
          <a
            class="button secondary"
            href={about?.sourceUrl ?? project}
            target="_blank"
            rel="noopener"><ExternalLink size={18} /> Source code</a
          >
          <a
            class="button secondary"
            href="{project}/issues"
            target="_blank"
            rel="noopener"><ExternalLink size={18} /> Report a problem</a
          >
        </div>
      </div>
    </section>

    <section>
      <div class="form-card stack">
        <h2 class="form-heading">Built with</h2>
        <p class="muted">
          Hortiplanner stands on other people's open-source work. Thank you to
          everyone behind it.
        </p>
        <ul class="credits">
          {#each parts as part (part.name)}
            <li>
              <strong>{part.name}</strong>
              <span class="muted">{part.use}, {part.licence} licence</span>
            </li>
          {/each}
        </ul>
      </div>
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
