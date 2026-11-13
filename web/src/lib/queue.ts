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
  stopped = false;
  private readonly failedKey: string;

  constructor(
    private readonly store: Store,
    owner: string,
  ) {
    this.waitingKey = `growlog.queue.${owner}`;
    this.failedKey = `growlog.failed.${owner}`;
  }

  private state(): { waiting: QueuedWork[]; failed: FailedWork[] } {
    try {
      const stored = JSON.parse(this.store.getItem(this.waitingKey) ?? "[]");
      // Earlier apps used separate keys. Upgrade on the first successful write.
      if (Array.isArray(stored)) {
        const failed = JSON.parse(this.store.getItem(this.failedKey) ?? "[]");
        return { waiting: stored, failed: Array.isArray(failed) ? failed : [] };
      }
      return { waiting: stored.waiting ?? [], failed: stored.failed ?? [] };
    } catch {
      return { waiting: [], failed: [] };
    }
  }

  private save(waiting: QueuedWork[], failed = this.failed) {
    // One storage write makes a transition indivisible, including when storage is full.
    this.store.setItem(this.waitingKey, JSON.stringify({ waiting, failed }));
  }

  private get items(): QueuedWork[] {
    return this.state().waiting;
  }

  get waiting(): number {
    return this.items.length;
  }

  get failed(): FailedWork[] {
    return this.state().failed;
  }

  get next(): QueuedWork | undefined {
    return this.items[0];
  }

  has(eventId: string): boolean {
    return this.items.some((item) => item.request.body.eventId === eventId);
  }

  add(request: WorkRequest) {
    this.save([...this.items, { request, description: fallbackDescription }]);
  }

  describe(eventId: string, description: string) {
    this.save(
      this.items.map((item) =>
        item.request.body.eventId === eventId ? { ...item, description } : item,
      ),
    );
  }

  remove(eventId: string) {
    this.save(
      this.items.filter((item) => item.request.body.eventId !== eventId),
    );
  }

  fail(item: QueuedWork, reason: string) {
    this.save(
      this.items.filter(
        (other) => other.request.body.eventId !== item.request.body.eventId,
      ),
      [
        ...this.failed,
        {
          eventId: item.request.body.eventId,
          description: item.description,
          reason,
          request: item.request,
        },
      ],
    );
  }

  /** Puts work that couldn't be saved back in line to send again. */
  retry(eventId: string) {
    const item = this.failed.find((failed) => failed.eventId === eventId);
    if (item?.request) {
      this.save(
        [
          ...this.items,
          { request: item.request, description: item.description },
        ],
        this.failed.filter((other) => other.eventId !== eventId),
      );
    }
  }

  /** Takes over work kept under another key, as queues once were kept by name. */
  adopt(owner: string) {
    const previous = new WorkQueue(this.store, owner);

    if (previous.waitingKey === this.waitingKey) {
      return;
    }

    const waiting = previous.items;
    const failed = previous.failed;

    // Copy before clearing; a failed clear is safe to retry without duplicating work.
    this.save(
      [
        ...this.items,
        ...waiting.filter((item) => !this.has(item.request.body.eventId)),
      ],
      [
        ...this.failed,
        ...failed.filter(
          (item) =>
            !this.failed.some((other) => other.eventId === item.eventId),
        ),
      ],
    );
    previous.save([], []);
  }

  dismiss(eventId: string) {
    this.save(
      this.items,
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
  for (let item = queue.next; item && !queue.stopped; item = queue.next) {
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

export function stopQueue() {
  if (active) active.stopped = true;
  active = null;
}

const nameKey = (owner: string) => `growlog.queueName.${owner}`;

export function useQueue(owner: string, name?: string): WorkQueue {
  stopQueue();
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
      const count = Array.isArray(items)
        ? items.length
        : ((items as { waiting?: unknown[] })?.waiting?.length ?? 0);

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
  const originating = active;
  const outcome = await submit(item);

  if (outcome.kind === "unreachable" && originating) {
    try {
      originating.add(item);
    } catch {
      return { kind: "storageFailed" };
    }
    return { kind: "queued", eventId: item.body.eventId };
  }

  return outcome;
}
