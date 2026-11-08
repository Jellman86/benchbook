export interface Me {
  name: string;
  isOwner: boolean;
}

export interface Person {
  id: string;
  name: string;
}

export interface Invite {
  userId: string;
  token: string;
}

export type Start =
  | { screen: "invite"; invite: Invite }
  | { screen: "home"; me: Me }
  | { screen: "bench"; people: Person[] }
  | { screen: "setup" }
  | { screen: "signIn" };

export interface Probes {
  me(): Promise<Me | null>;
  benchPeople(): Promise<Person[] | null>;
  needsSetup(): Promise<boolean>;
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

/** Works out the first screen. A person signed in comes first, then a bench station, then setup or sign-in. */
export async function chooseStart(
  probes: Probes,
  invite: Invite | null,
): Promise<Start> {
  if (invite) {
    return { screen: "invite", invite };
  }

  const me = await probes.me();

  if (me) {
    return { screen: "home", me };
  }

  const people = await probes.benchPeople();

  if (people) {
    return { screen: "bench", people };
  }

  return (await probes.needsSetup())
    ? { screen: "setup" }
    : { screen: "signIn" };
}
