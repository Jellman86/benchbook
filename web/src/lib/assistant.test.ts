import { describe, expect, it } from "vitest";
import { answerBlocks } from "./assistant.svelte";

describe("answerBlocks", () => {
  it("turns an answer into paragraphs and lists, never markup", () => {
    expect(
      answerBlocks(
        "## Check these\n\n- **C000012** looks dry\n- <img src=x onerror=alert(1)>\n\nWater in the morning.",
      ),
    ).toEqual([
      { kind: "p", text: "Check these" },
      {
        kind: "list",
        items: ["C000012 looks dry", "<img src=x onerror=alert(1)>"],
      },
      { kind: "p", text: "Water in the morning." },
    ]);
  });
});
