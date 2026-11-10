import { submit, type Outcome, type WorkRequest } from "./work";

interface QueuedWork {
  request: WorkRequest;
  description: string;
}

export interface FailedWork {
  eventId: string;
  description: string;
  reason: string;
  /** What was sent, so it can be tried again. Missing on work set aside by older versions. */
  request?: WorkRequest;
}

type Store = Pick<Storage, "getItem" | "setItem">;

const fallbackDescription = "Saved work";

/**
 * Work waiting to reach the server. Kept per person, so that anything waiting
 * on a shared device is only ever sent under the account of whoever recorded it.
 */
export class WorkQueue {
  private readonly waitingKey: string;
  private readonly failedKey: string;

  constructor(
    private readonly store: Store,
    owner: string,
  ) {
    this.waitingKey = `growlog.queue.${owner}`;
    this.failedKey = `growlog.failed.${owner}`;
  }

  // Storage that's full, blocked or damaged must not stop the app opening.
  private read<T>(key: string): T[] {
    try {
      const items = JSON.parse(this.store.getItem(key) ?? "[]") as unknown;
      return Array.isArray(items) ? (items as T[]) : [];
    } catch {
      return [];
    }
  }

  private write<T>(key: string, items: T[]) {
    this.store.setItem(key, JSON.stringify(items));
  }

  private get items(): QueuedWork[] {
    return this.read<QueuedWork>(this.waitingKey);
  }

  get waiting(): number {
    return this.items.length;
  }

  get failed(): FailedWork[] {
    return this.read<FailedWork>(this.failedKey);
  }

  get next(): QueuedWork | undefined {
    return this.items[0];
  }

  has(eventId: string): boolean {
    return this.items.some((item) => item.request.body.eventId === eventId);
  }

  add(request: WorkRequest) {
    this.write(this.waitingKey, [
      ...this.items,
      { request, description: fallbackDescription },
    ]);
  }

  describe(eventId: string, description: string) {
    this.write(
      this.waitingKey,
      this.items.map((item) =>
        item.request.body.eventId === eventId ? { ...item, description } : item,
      ),
    );
  }

  remove(eventId: string) {
    this.write(
      this.waitingKey,
      this.items.filter((item) => item.request.body.eventId !== eventId),
    );
  }

  fail(item: QueuedWork, reason: string) {
    this.remove(item.request.body.eventId);
    this.write(this.failedKey, [
      ...this.failed,
      {
        eventId: item.request.body.eventId,
        description: item.description,
        reason,
        request: item.request,
      },
    ]);
  }

  /** Puts work that couldn't be saved back in line to send again. */
  retry(eventId: string) {
    const item = this.failed.find((failed) => failed.eventId === eventId);

    if (!item?.request) {
      return;
    }

    this.dismiss(eventId);
    this.write(this.waitingKey, [
      ...this.items,
      { request: item.request, description: item.description },
    ]);
  }

  /** Takes over work kept under another key, as queues once were kept by name. */
  adopt(owner: string) {
    const previous = new WorkQueue(this.store, owner);

    if (previous.waitingKey === this.waitingKey) {
      return;
    }

    const waiting = previous.items;
    const failed = previous.failed;

    if (waiting.length > 0) {
      this.write(this.waitingKey, [...this.items, ...waiting]);
      previous.write(previous.waitingKey, []);
    }

    if (failed.length > 0) {
      this.write(this.failedKey, [...this.failed, ...failed]);
      previous.write(previous.failedKey, []);
    }
  }

  dismiss(eventId: string) {
    this.write(
      this.failedKey,
      this.failed.filter((item) => item.eventId !== eventId),
    );
  }
}

/** Sends waiting work in order, stopping as soon as sending is impossible. */
export async function flush(
  queue: WorkQueue,
  send: (request: WorkRequest) => Promise<Outcome>,
  explain: (outcome: Outcome) => string,
): Promise<void> {
  for (let item = queue.next; item; item = queue.next) {
    const outcome = await send(item.request);

    if (outcome.kind === "stored") {
      queue.remove(item.request.body.eventId);
    } else if (outcome.kind === "unreachable" || outcome.kind === "signedOut") {
      return;
    } else {
      // Kept where the person can see it, as it can't be sent as it stands.
      queue.fail(item, explain(outcome));
    }
  }
}

let active: WorkQueue | null = null;

const nameKey = (owner: string) => `growlog.queueName.${owner}`;

export function useQueue(owner: string, name?: string): WorkQueue {
  active = new WorkQueue(localStorage, owner);

  try {
    if (name) {
      localStorage.setItem(nameKey(owner), name);
    }
  } catch {
    // Without storage, other people's waiting work simply isn't named.
  }

  return active;
}

/** Work other people recorded on this device that hasn't been sent, so it isn't forgotten. */
export function waitingByOthers(
  me: string | null,
  store: Pick<Storage, "getItem" | "key" | "length"> = localStorage,
): { owner: string; name: string; count: number }[] {
  const found: { owner: string; name: string; count: number }[] = [];

  try {
    for (let index = 0; index < store.length; index += 1) {
      const key = store.key(index) ?? "";
      const owner = key.startsWith("growlog.queue.")
        ? key.slice("growlog.queue.".length)
        : null;

      if (!owner || owner === me) {
        continue;
      }

      const items = JSON.parse(store.getItem(key) ?? "[]") as unknown;
      const count = Array.isArray(items) ? items.length : 0;

      if (count > 0) {
        found.push({
          owner,
          name: store.getItem(nameKey(owner)) ?? "someone else",
          count,
        });
      }
    }
  } catch {
    return found;
  }

  return found;
}

/** Sends work now, or keeps it on this device if the server can't be reached. */
export async function recordWork(item: WorkRequest): Promise<Outcome> {
  const outcome = await submit(item);

  if (outcome.kind === "unreachable" && active) {
    active.add(item);
    return { kind: "queued", eventId: item.body.eventId };
  }

  return outcome;
}
