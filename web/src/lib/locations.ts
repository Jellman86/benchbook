interface Place {
  id: string;
  name: string;
  parentId?: string | null;
}

/** Puts places in reading order, each one straight after the place it's inside. */
export function nestLocations<T extends Place>(
  places: T[],
): { place: T; depth: number }[] {
  const known = new Set(places.map((place) => place.id));
  const childrenOf = new Map<string | null, T[]>();

  for (const place of places) {
    const parent =
      place.parentId && known.has(place.parentId) ? place.parentId : null;
    childrenOf.set(parent, [...(childrenOf.get(parent) ?? []), place]);
  }

  const nested: { place: T; depth: number }[] = [];

  const visit = (parent: string | null, depth: number) => {
    const children = [...(childrenOf.get(parent) ?? [])].sort((a, b) =>
      a.name.localeCompare(b.name),
    );

    for (const place of children) {
      nested.push({ place, depth });
      visit(place.id, depth + 1);
    }
  };

  visit(null, 0);
  return nested;
}
