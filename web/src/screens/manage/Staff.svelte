<script lang="ts">
  import FormPanel from "../FormPanel.svelte";
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, type Schemas } from "../../lib/types";

  let { onBack }: { onBack: () => void } = $props();

  let staff = $state<Schemas["StaffMember"][]>([]);
  let name = $state("");
  let pin = $state("");
  let problem = $state("");
  let link = $state("");
  let changingPinFor = $state<string | null>(null);
  let newPin = $state("");

  const pinProblem = "A PIN must be 4 to 6 digits.";
  const setProblem = (message: string) => (problem = message);

  async function load() {
    staff = (await api.GET("/api/staff")).data ?? [];
  }

  onMount(() => void attempt(load, setProblem));

  const add = (event: SubmitEvent) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/staff", {
        body: { name, pin },
      });

      if (response.status === 409) {
        problem = "Someone with that name is already on the list.";
      } else if (!response.ok) {
        problem = pinProblem;
      } else {
        name = "";
        pin = "";
        await load();
      }
    }, setProblem);
  };

  const invite = (id: string) =>
    attempt(async () => {
      const { data } = await api.POST("/api/staff/{id}/invite", {
        params: { path: { id } },
      });

      if (data) {
        link = `${location.origin}/invite?user=${id}&token=${encodeURIComponent(data.token)}`;
      }
    }, setProblem);

  const savePin = (event: SubmitEvent, id: string) => {
    event.preventDefault();

    return attempt(async () => {
      const { response } = await api.POST("/api/staff/{id}/pin", {
        params: { path: { id } },
        body: { pin: newPin },
      });

      if (response.ok) {
        changingPinFor = null;
        newPin = "";
      } else {
        problem = pinProblem;
      }
    }, setProblem);
  };

  const archive = (member: Schemas["StaffMember"]) => {
    if (
      !confirm(`${member.name} has left? They'll no longer be able to sign in.`)
    ) {
      return;
    }

    return attempt(async () => {
      const { response } = await api.POST("/api/staff/{id}/archive", {
        params: { path: { id: member.id } },
      });

      if (response.ok) {
        await load();
      } else {
        problem = "You can't remove your own account.";
      }
    }, setProblem);
  };
</script>

<main>
  <h1>Staff</h1>

  <div class="split">
    <section>
      <FormPanel label="Add a person">
        <form class="stack" onsubmit={add}>
          <label>Name <input bind:value={name} required /></label>
          <label
            >PIN for the bench (4 to 6 digits)
            <input
              bind:value={pin}
              inputmode="numeric"
              autocomplete="off"
              required
            />
          </label>
          <button type="submit">Add</button>
        </form>
      </FormPanel>
    </section>
    <section>
      {#if problem}<p class="problem" role="alert">{problem}</p>{/if}

      {#if link}
        <div class="card">
          <p>Send this link to them. It works once, for 7 days:</p>
          <p class="link" data-testid="invite-link">{link}</p>
        </div>
      {/if}

      {#each staff.filter((member) => !member.archived) as member (member.id)}
        <div class="card">
          <strong>{member.name}</strong>
          {#if changingPinFor === member.id}
            <form class="stack" onsubmit={(event) => savePin(event, member.id)}>
              <label
                >New PIN
                <input
                  bind:value={newPin}
                  inputmode="numeric"
                  autocomplete="off"
                  required
                />
              </label>
              <button type="submit">Save PIN</button>
            </form>
          {:else}
            <div class="row">
              <button class="secondary" onclick={() => invite(member.id)}
                >Phone invitation</button
              >
              <button
                class="secondary"
                onclick={() => (changingPinFor = member.id)}>New PIN</button
              >
              <button class="secondary" onclick={() => archive(member)}
                >Has left</button
              >
            </div>
          {/if}
        </div>
      {/each}
    </section>
  </div>

  <button class="back mobile-only" onclick={onBack}>Back</button>
</main>
