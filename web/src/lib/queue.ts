import { submit, type Outcome, type WorkRequest } from "./work";

interface QueuedWork {
  request: WorkRequest;
  description: string;
}

export interface FailedWork {
  eventId: string;
  description: string;
  reason: string;
}

type Store = Pick<Storage, "getItem" | "setItem">;

const fallbackDescription = "Saved work";

/**
 * Work waiting to reach the server. Kept per person, so that anything waiting
 * on a shared device is only ever sent under the name of whoever recorded it.
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

  private read<T>(key: string): T[] {
    return JSON.parse(this.store.getItem(key) ?? "[]") as T[];
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
      },
    ]);
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

export function useQueue(owner: string): WorkQueue {
  active = new WorkQueue(localStorage, owner);
  return active;
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
