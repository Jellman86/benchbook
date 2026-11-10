import { beforeEach, describe, expect, it } from "vitest";
import { flush, waitingByOthers, WorkQueue } from "./queue";
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
