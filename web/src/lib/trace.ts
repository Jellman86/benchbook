import type { Schemas } from "./types";
import { momentLabel } from "./dates";
import { lossReasons } from "./work";

export type BatchTrace = Schemas["BatchTrace"];
export type TraceStep = Schemas["TraceStep"];

export const sourceLabels: Record<Schemas["BatchSource"], string> = {
  SeedLot: "Sown from seed",
  BoughtIn: "Bought in as plants",
  Cuttings: "Raised from cuttings",
};

export const passportStatusLabels: Record<
  Schemas["ReceivedPassportStatus"],
  string
> = {
  Held: "Held",
  UsedUp: "Used up",
  NoLongerValid: "No longer valid",
};

/** One line of the batch's record, as an inspector would read it. */
export function describeStep(step: TraceStep): string {
  const quantity = Math.abs(Number(step.quantity ?? 0));
  const details = step.details as Record<string, unknown> | null;
  const text = (name: string) =>
    typeof details?.[name] === "string" ? (details[name] as string) : "";
  const into = step.labelCode ? ` into ${step.labelCode}` : "";
  const from = step.fromLabelCode ? ` from ${step.fromLabelCode}` : "";

  switch (step.type) {
    case "Sown":
      return `${quantity} sown${into}`;
    case "Received":
      return `${quantity} arrived${into}`;
    case "CuttingsTaken":
      return `${quantity} cuttings taken${into}`;
    case "Germinated":
      return "Germinated";
    case "PrickedOut":
      return `${quantity} pricked out${from}${into}`;
    case "PottedOn":
      return `${quantity} potted on${from}${into}`;
    case "Split":
      return `${quantity} split off${step.labelCode ? ` into ${step.labelCode}` : ""}`;
    case "Moved":
      return `Moved to ${step.place ?? "another place"}`;
    case "Lost": {
      const reason = lossReasons.find(
        (item) => item.reason === details?.reason,
      )?.label;
      return reason ? `${quantity} lost (${reason})` : `${quantity} lost`;
    }
    case "Sold":
      return `${quantity} sold`;
    case "Treated": {
      const dose = text("dose");
      const area = text("area");
      return [
        `Treated with ${text("product") || "a product"}`,
        dose && `at ${dose}`,
        area && `on ${area}`,
      ]
        .filter(Boolean)
        .join(", ");
    }
    case "Examined":
      return "Examined";
    case "StockCounted": {
      const counted = details?.counted;
      return typeof counted === "number" ? `Counted: ${counted}` : "Counted";
    }
    case "Note":
      return `Note: ${text("note")}`;
    case "Ready":
      return "Marked ready";
    default:
      return String(step.type);
  }
}

/** The container a step happened in, when its description doesn't already say. */
export function containerOf(step: TraceStep): string | null {
  return step.labelCode && !describeStep(step).includes(step.labelCode)
    ? step.labelCode
    : null;
}

const sourceNames: Record<string, string> = {
  SeedLot: "Seed",
  BoughtIn: "Bought in",
  Cuttings: "Cuttings",
};

/** A corrected value in words: moments as days, sources as people say them, and blanks as "not set". */
export function correctionValue(
  correction: Schemas["TraceCorrection"],
  side: "before" | "after",
): string {
  const value = correction[side];

  if (!value) {
    return "not set";
  }

  if (correction.field === "Started") {
    return momentLabel(value);
  }

  return correction.field === "Source" ? (sourceNames[value] ?? value) : value;
}

/** Plants still growing, in the containers that hold them. */
export const stillGrowing = (trace: BatchTrace) =>
  trace.containers.filter((container) => Number(container.now) > 0);
