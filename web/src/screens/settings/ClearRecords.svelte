<script lang="ts">
  import { api } from "../../api/client";
  import { attempt } from "../../lib/types";

  // Empties the nursery's records so the owner can start afresh, keeping people and settings.
  const words = "clear everything";

  let typed = $state("");
  let busy = $state(false);
  let problem = $state("");

  const ready = $derived(typed.trim().toLowerCase() === words);

  const clear = (event: SubmitEvent) => {
    event.preventDefault();

    if (
      !confirm(
        "Clear every record? This can't be undone, apart from restoring a backup.",
      )
    ) {
      return;
    }

    return attempt(
      async () => {
        busy = true;

        try {
          const { response } = await api.POST("/api/settings/clear-records", {
            body: { confirmation: typed },
          });

          if (response.ok) {
            window.location.assign("/");
          } else {
            problem = "The records couldn't be cleared.";
          }
        } finally {
          busy = false;
        }
      },
      (message) => (problem = message),
    );
  };
</script>

<details class="form-card start-afresh">
  <summary><h2 class="form-heading">Start afresh</h2></summary>
  <form class="stack" onsubmit={clear}>
    <p>
      This clears every plant, batch, tray, place, pot size, supplier, purchase,
      till taking, plant passport, customer and invoice, including example data.
    </p>
    <p class="muted">
      It keeps everyone's sign-in, staff, bench devices, the nursery's details,
      logo and label designs. Plant passport records normally have to be kept
      for three years, so only do this before real records go in.
    </p>
    {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
    <label>
      Type <strong>{words}</strong> to confirm
      <input bind:value={typed} autocomplete="off" />
    </label>
    <button type="submit" class="danger" disabled={busy || !ready}
      >Clear all records</button
    >
  </form>
</details>
