/** The lists whose entries are archived rather than deleted, so older records keep their meaning. */
export type ArchivableList = "plants" | "pot-sizes" | "suppliers" | "locations";

interface Entry {
  id: string;
  name?: string;
  commonName?: string;
  variety?: string | null;
  archived: boolean;
}

export interface Archived {
  id: string;
  name: string;
}

/** Plain words for why a place can't stop being used. */
export function refusalWords(problem: string, count: number): string {
  if (problem === "HasPlaces") {
    return count === 1
      ? "It still has a place inside it. Move that place, or stop using it, first."
      : `It still has ${count} places inside it. Move them, or stop using them, first.`;
  }

  return count === 1
    ? "It still has a tray on it. Move the tray somewhere else first."
    : `It still has ${count} trays on it. Move them somewhere else first.`;
}

/**
 * Asks, then stops an entry being offered. Gives a reason when it wasn't
 * stopped, or null when it was (or the person changed their mind).
 */
export async function stopUsing(
  list: ArchivableList,
  id: string,
  name: string,
): Promise<string | null> {
  if (
    !confirm(
      `Stop using ${name}? It won't be offered any more, but everything recorded about it is kept and you can bring it back.`,
    )
  ) {
    return null;
  }

  const response = await fetch(`/api/${list}/${id}/archive`, {
    method: "POST",
  });

  if (response.status === 409) {
    const refused = (await response.json()) as {
      problem: string;
      count: number;
    };
    return `You can't stop using ${name} yet.  ${refusalWords(refused.problem, refused.count)}`;
  }

  return response.ok ? null : "That didn't work. Please try again.";
}

export async function restore(list: ArchivableList, id: string) {
  await fetch(`/api/${list}/${id}/restore`, { method: "POST" });
}

export async function listArchived(list: ArchivableList): Promise<Archived[]> {
  const response = await fetch(`/api/${list}?includeArchived=true`);
  const entries = response.ok ? ((await response.json()) as Entry[]) : [];

  return entries
    .filter((entry) => entry.archived)
    .map((entry) => ({
      id: entry.id,
      name:
        entry.name ??
        (entry.variety
          ? `${entry.commonName} '${entry.variety}'`
          : (entry.commonName ?? "")),
    }));
}
