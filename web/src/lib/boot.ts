export interface Me {
  id: string;
  name: string;
  isOwner: boolean;
  /** Signed in as the nursery team, because the nursery chose no signing in. */
  isTeam?: boolean;
  /** When their picture last changed, or null if they have none. */
  pictureAt?: string | null;
}

/** Set on this device when someone chooses to sign in as themselves rather than as the nursery team. */
export const signInAsYourselfKey = "hortiplanner.signInAsYourself";

export interface Person {
  id: string;
  name: string;
  pictureAt?: string | null;
}

export interface Invite {
  userId: string;
  token: string;
}

export type Start =
  | { screen: "invite"; invite: Invite }
  | { screen: "home"; me: Me; atBench: boolean }
  | { screen: "bench"; people: Person[] }
  | { screen: "setup" }
  | { screen: "signIn" };

export interface Probes {
  me(): Promise<Me | null>;
  benchPeople(): Promise<Person[] | null>;
  needsSetup(): Promise<boolean>;
  /** Goes straight in as the nursery team, where the nursery chose no signing in. */
  teamSignIn(): Promise<boolean>;
}

/** Reads an invitation from a link like /invite?user=...&token=... */
export function readInvite(path: string, search: string): Invite | null {
  if (path !== "/invite") {
    return null;
  }

  const query = new URLSearchParams(search);
  const userId = query.get("user");
  const token = query.get("token");

  return userId && token ? { userId, token } : null;
}

/** Plain words for why signing in through the shared staff login didn't work, if it didn't. */
export function externalSignInProblem(search: string): string {
  switch (new URLSearchParams(search).get("sso")) {
    case null:
      return "";
    case "unavailable":
      return "The staff login isn't available just now. Use another way to sign in.";
    case "name-taken":
      return "Someone here already has that name. Sign in another way, then connect your staff login under your settings.";
    case "refused":
      return "That account can't sign in. Ask the owner to check it.";
    case "already-linked":
      return "That staff login is already connected to someone else's account.";
    default:
      return "Signing in didn't work. Please try again.";
  }
}

/** Works out the first screen. A person signed in comes first, then a bench station, then setup or sign-in. */
export async function chooseStart(
  probes: Probes,
  invite: Invite | null,
): Promise<Start> {
  if (invite) {
    return { screen: "invite", invite };
  }

  const me = await probes.me();
  const people = await probes.benchPeople();

  if (me) {
    return { screen: "home", me, atBench: people !== null };
  }

  if (people) {
    return { screen: "bench", people };
  }

  if (await probes.needsSetup()) {
    return { screen: "setup" };
  }

  const team = (await probes.teamSignIn()) ? await probes.me() : null;

  return team
    ? { screen: "home", me: team, atBench: false }
    : { screen: "signIn" };
}
