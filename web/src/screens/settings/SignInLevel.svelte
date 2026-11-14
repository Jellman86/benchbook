<script lang="ts">
  import { onMount } from "svelte";
  import { api } from "../../api/client";
  import { attempt, type Schemas } from "../../lib/types";

  // How much signing in the nursery wants. Only Hortiplanner's own sign-in is affected.
  let level = $state<Schemas["SignInLevel"] | null>(null);
  let problem = $state("");

  const levels: {
    level: Schemas["SignInLevel"];
    title: string;
    detail: string;
  }[] = [
    {
      level: "Open",
      title: "No signing in",
      detail:
        "Devices go straight in. Work is recorded as the nursery team, so you won't see who did what.",
    },
    {
      level: "Names",
      title: "Tap your name",
      detail:
        "Everyone taps their name, with nothing to type, at a bench too. Work shows who did it.",
    },
    {
      level: "Personal",
      title: "Each person's own way",
      detail:
        "Each person signs in the way you set for them under Staff. A bench asks for a PIN.",
    },
    {
      level: "Strict",
      title: "Strict",
      detail:
        "A password, passkey or staff login for everyone, and a PIN at a bench.",
    },
  ];

  const looser: Record<string, number> = {
    Open: 0,
    Names: 1,
    Personal: 2,
    Strict: 3,
  };

  const setProblem = (message: string) => (problem = message);

  async function load() {
    level =
      (await api.GET("/api/staff/sign-in-settings")).data?.level ?? "Personal";
  }

  onMount(() => void attempt(load, setProblem));

  const setLevel = (chosen: Schemas["SignInLevel"]) =>
    attempt(async () => {
      if (
        level !== null &&
        looser[chosen] < looser[level] &&
        !confirm(
          chosen === "Open"
            ? "Let any device on the nursery's network straight in, with no signing in? Only do this if Hortiplanner can't be reached from outside the nursery."
            : "Let staff sign in by tapping their name, with nothing to type? Only do this if Hortiplanner can't be reached from outside the nursery.",
        )
      ) {
        await load();
        return;
      }

      level = chosen;
      await api.PUT("/api/staff/sign-in-settings", { body: { level: chosen } });
      await load();
    }, setProblem);
</script>

<fieldset class="form-card sign-in-levels">
  <legend>How much signing in</legend>
  {#if problem}<p class="problem" role="alert">{problem}</p>{/if}
  <div class="stack">
    {#each levels as option (option.level)}
      <label class="level-choice" class:chosen={level === option.level}>
        <input
          type="radio"
          name="sign-in-level"
          checked={level === option.level}
          disabled={level === null}
          onchange={() => setLevel(option.level)}
        />
        <span class="stack-tight">
          <strong>{option.title}</strong>
          <span class="muted">{option.detail}</span>
        </span>
      </label>
    {/each}
  </div>
  <p class="muted credit">
    Your own account always needs your password or passkey, so the nursery's
    settings stay yours.
  </p>
</fieldset>
