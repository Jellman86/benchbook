import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  flush,
  recordWork,
  stopQueue,
  useQueue,
  waitingByOthers,
  WorkQueue,
} from "./queue";
import type { Outcome, WorkRequest } from "./work";

function memoryStore() {
  const values = new Map<string, string>();

  return {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => void values.set(key, value),
  };
}

const request = (eventId: string): WorkRequest => ({
  url: "/api/thing",
  body: { eventId, occurredAt: "2026-11-08T10:00:00Z" },
});

const explain = () => "It went wrong";

describe("WorkQueue", () => {
  let store: ReturnType<typeof memoryStore>;
  let queue: WorkQueue;

  beforeEach(() => {
    store = memoryStore();
    queue = new WorkQueue(store, "Alex");
  });

  it("keeps work in the order it was recorded", () => {
    queue.add(request("a"));
    queue.add(request("b"));

    expect(queue.waiting).toBe(2);
    expect(queue.next?.request.body.eventId).toBe("a");
  });

  it("survives the app closing", () => {
    queue.add(request("a"));

    expect(new WorkQueue(store, "Alex").waiting).toBe(1);
  });

  it("keeps each person's work separate on a shared device", () => {
    queue.add(request("a"));

    expect(new WorkQueue(store, "Sam").waiting).toBe(0);
  });

  it("removes work, as an undo does", () => {
    queue.add(request("a"));

    queue.remove("a");

    expect(queue.has("a")).toBe(false);
  });

  it("keeps a description for showing later", () => {
    queue.add(request("a"));
    queue.describe("a", "Lost 3 Lavender");

    expect(queue.next?.description).toBe("Lost 3 Lavender");
  });

  describe("flush", () => {
    it("sends everything in order and empties the queue", async () => {
      queue.add(request("a"));
      queue.add(request("b"));
      const sent: string[] = [];

      await flush(
        queue,
        async (item) => {
          sent.push(item.body.eventId);
          return { kind: "stored", eventId: item.body.eventId };
        },
        explain,
      );

      expect(sent).toEqual(["a", "b"]);
      expect(queue.waiting).toBe(0);
    });

    it.each<Outcome>([{ kind: "unreachable" }, { kind: "signedOut" }])(
      "stops and keeps everything when the outcome is $kind",
      async (outcome) => {
        queue.add(request("a"));
        queue.add(request("b"));
        let attempts = 0;

        await flush(
          queue,
          async () => {
            attempts++;
            return outcome;
          },
          explain,
        );

        expect(attempts).toBe(1);
        expect(queue.waiting).toBe(2);
      },
    );

    it("sets aside work the server refuses and carries on with the rest", async () => {
      queue.add(request("a"));
      queue.describe("a", "Lost 99 Lavender");
      queue.add(request("b"));

      await flush(
        queue,
        async (item) =>
          item.body.eventId === "a"
            ? { kind: "refused", problem: "BelowZero" }
            : { kind: "stored", eventId: "b" },
        explain,
      );

      expect(queue.waiting).toBe(0);
      expect(queue.failed).toEqual([
        {
          eventId: "a",
          description: "Lost 99 Lavender",
          reason: "It went wrong",
          request: request("a"),
        },
      ]);
    });

    it("lets a person dismiss work that couldn't be saved", async () => {
      queue.add(request("a"));
      await flush(queue, async () => ({ kind: "rejected" }), explain);

      queue.dismiss("a");

      expect(queue.failed).toEqual([]);
    });

    it("puts work that couldn't be saved back in line to try again", async () => {
      queue.add(request("a"));
      await flush(queue, async () => ({ kind: "rejected" }), explain);

      queue.retry("a");

      expect(queue.failed).toEqual([]);
      expect(queue.next?.request).toEqual(request("a"));
    });
  });

  it("takes over work kept under a person's old key", () => {
    const old = new WorkQueue(store, "Alex Reed");
    old.add(request("a"));

    queue.adopt("Alex Reed");

    expect(queue.waiting).toBe(1);
    expect(old.waiting).toBe(0);
  });

  it("finds work other people left waiting on the device", () => {
    const other = new WorkQueue(store, "kim");
    other.add(request("k"));
    store.setItem("growlog.queueName.kim", "Kim Bower");
    const keys = [
      "growlog.queue.kim",
      "growlog.queue.Alex",
      "growlog.queueName.kim",
    ];
    const listing = {
      getItem: store.getItem,
      key: (index: number) => keys[index] ?? null,
      length: keys.length,
    };

    expect(waitingByOthers("Alex", listing)).toEqual([
      { owner: "kim", name: "Kim Bower", count: 1 },
    ]);
  });

  it("opens even when what's stored is damaged", () => {
    store.setItem("growlog.queue.Alex", "not json");
    expect(new WorkQueue(store, "Alex").waiting).toBe(0);
  });
});

describe("queue storage transitions", () => {
  it("keeps the waiting item if writing its refusal fails", () => {
    const store = memoryStore();
    const queue = new WorkQueue(store, "Alex");
    queue.add(request("a"));
    const item = queue.next!;
    const failing = new WorkQueue(
      {
        getItem: store.getItem,
        setItem: () => {
          throw new Error("full");
        },
      },
      "Alex",
    );
    expect(() => failing.fail(item, "refused")).toThrow("full");
    expect(queue.waiting).toBe(1);
    expect(queue.failed).toEqual([]);
  });

  it("keeps the failed item if putting it back in line fails", () => {
    const store = memoryStore();
    const queue = new WorkQueue(store, "Alex");
    queue.add(request("a"));
    queue.fail(queue.next!, "refused");
    const failing = new WorkQueue(
      {
        getItem: store.getItem,
        setItem: () => {
          throw new Error("full");
        },
      },
      "Alex",
    );
    expect(() => failing.retry("a")).toThrow("full");
    expect(queue.waiting).toBe(0);
    expect(queue.failed[0].eventId).toBe("a");
  });

  it("upgrades both old keys in one write", () => {
    const store = memoryStore();
    store.setItem(
      "growlog.queue.Alex",
      JSON.stringify([{ request: request("a"), description: "a" }]),
    );
    store.setItem(
      "growlog.failed.Alex",
      JSON.stringify([
        { eventId: "b", request: request("b"), description: "b", reason: "no" },
      ]),
    );
    const queue = new WorkQueue(store, "Alex");
    queue.retry("b");
    expect(queue.waiting).toBe(2);
    expect(queue.failed).toEqual([]);
    expect(new WorkQueue(store, "Alex").failed).toEqual([]);
  });

  it("stops before sending the next item when the person leaves", async () => {
    const queue = new WorkQueue(memoryStore(), "Alex");
    queue.add(request("a"));
    queue.add(request("b"));
    const sent: string[] = [];
    await flush(
      queue,
      async (item) => {
        sent.push(item.body.eventId);
        queue.stopped = true;
        return { kind: "stored", eventId: item.body.eventId };
      },
      explain,
    );
    expect(sent).toEqual(["a"]);
    expect(queue.next?.request.body.eventId).toBe("b");
  });
});

describe("originating queue", () => {
  afterEach(() => {
    stopQueue();
    vi.unstubAllGlobals();
  });

  it("keeps a lost reply under the person who started the request", async () => {
    vi.stubGlobal("localStorage", memoryStore());
    let reject!: (reason: Error) => void;
    vi.stubGlobal(
      "fetch",
      () =>
        new Promise((_, failed) => {
          reject = failed;
        }),
    );
    const alex = useQueue("Alex");
    const pending = recordWork(request("a"));
    const kim = useQueue("Kim");
    reject(new Error("offline"));
    expect(await pending).toEqual({ kind: "queued", eventId: "a" });
    expect(alex.has("a")).toBe(true);
    expect(kim.waiting).toBe(0);
    expect(alex.stopped).toBe(true);
  });

  it("reports a failure to store offline work", async () => {
    vi.stubGlobal("localStorage", {
      getItem: () => null,
      setItem: () => {
        throw new Error("full");
      },
    });
    vi.stubGlobal("fetch", () => Promise.reject(new Error("offline")));
    useQueue("Alex");
    expect(await recordWork(request("a"))).toEqual({ kind: "storageFailed" });
  });
});
