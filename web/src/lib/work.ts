import { sessionEnded } from "../api/client";
import type { Schemas } from "./types";

/** A piece of work ready to send. Plain data, so it can wait in a queue. */
export interface WorkRequest {
  url: string;
  body: { eventId: string; occurredAt: string };
}

export type Outcome =
  | { kind: "stored"; eventId: string; labelCode?: string }
  | { kind: "queued"; eventId: string }
  | { kind: "signedOut" }
  | { kind: "refused"; problem: string }
  | { kind: "rejected" }
  | { kind: "unreachable" };

type Details<T> = Omit<T, "eventId" | "occurredAt">;

function request<T extends object>(url: string, details: T): WorkRequest {
  return {
    url,
    body: {
      eventId: crypto.randomUUID(),
      occurredAt: new Date().toISOString(),
      ...details,
    },
  };
}

const onContainer = (id: string, action: string) =>
  `/api/containers/${id}/${action}`;

export const work = {
  startBatch: (details: Details<Schemas["StartBatchRequest"]>) =>
    request("/api/batches", details),
  germinated: (id: string) => request(onContainer(id, "germinated"), {}),
  lose: (id: string, details: Details<Schemas["LossRequest"]>) =>
    request(onContainer(id, "losses"), details),
  move: (id: string, details: Details<Schemas["MoveRequest"]>) =>
    request(onContainer(id, "move"), details),
  transfer: (id: string, details: Details<Schemas["TransferRequest"]>) =>
    request(onContainer(id, "transfer"), details),
  count: (id: string, details: Details<Schemas["CountRequest"]>) =>
    request(onContainer(id, "count"), details),
  treat: (id: string, details: Details<Schemas["TreatmentRequest"]>) =>
    request(onContainer(id, "treatments"), details),
  note: (id: string, details: Details<Schemas["NoteRequest"]>) =>
    request(onContainer(id, "notes"), details),
  undo: (eventId: string) =>
    request(`/api/events/${eventId}/correct`, {
      quantity: null,
      locationId: null,
    }),
};

export async function submit(item: WorkRequest): Promise<Outcome> {
  let response: Response;

  try {
    response = await fetch(item.url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(item.body),
    });
  } catch {
    return { kind: "unreachable" };
  }

  if (response.status === 401) {
    window.dispatchEvent(new Event(sessionEnded));
    return { kind: "signedOut" };
  }

  if (response.ok) {
    const result = (await response.json()) as { labelCode?: string };
    return {
      kind: "stored",
      eventId: item.body.eventId,
      labelCode: result.labelCode,
    };
  }

  if (response.status === 409) {
    const result = (await response.json()) as { problem: string };
    return { kind: "refused", problem: result.problem };
  }

  return { kind: "rejected" };
}

export function isRecorded(
  outcome: Outcome,
): outcome is Extract<Outcome, { kind: "stored" | "queued" }> {
  return outcome.kind === "stored" || outcome.kind === "queued";
}

/** Plain words for an outcome that didn't record anything. */
export function outcomeProblem(outcome: Outcome): string {
  switch (outcome.kind) {
    case "stored":
    case "queued":
      return "";
    case "signedOut":
      return "You've been signed out. Please sign in again.";
    case "refused":
      return outcome.problem === "BelowZero"
        ? "There aren't that many here. Check the number, or count the stock."
        : "That has already been corrected.";
    case "unreachable":
      return "Can't reach GrowLog. Check the connection and try again.";
    case "rejected":
      return "That wasn't accepted. Please check it and try again.";
  }
}

export function plantName(commonName: string, variety?: string | null): string {
  return variety ? `${commonName} '${variety}'` : commonName;
}

/** A rate as a whole percentage, or a dash when there's nothing to go on yet. */
export function percent(rate: number | string | null | undefined): string {
  return rate === null || rate === undefined
    ? "–"
    : `${Math.round(Number(rate) * 100)}%`;
}

export function money(value: number): string {
  return `£${value.toFixed(2)}`;
}

export const lossReasons: { reason: Schemas["LossReason"]; label: string }[] = [
  { reason: "FailedToGerminate", label: "Didn't germinate" },
  { reason: "DampingOff", label: "Damping off" },
  { reason: "Pest", label: "Pest" },
  { reason: "Disease", label: "Disease" },
  { reason: "Frost", label: "Frost" },
  { reason: "Other", label: "Other" },
];

const startTypes = ["Sown", "Received", "CuttingsTaken"];

function lossLabel(reason: unknown): string {
  return lossReasons.find((item) => item.reason === reason)?.label ?? "";
}

/** One line a person can read about something they recorded. */
export function describeRecent(action: Schemas["RecentAction"]): string {
  const line = action.lines[0];
  const plant = line ? plantName(line.commonName, line.variety) : "";
  const quantity = line ? Math.abs(Number(line.quantityChange)) : 0;
  const where = action.locationName ? `, ${action.locationName}` : "";
  const details = action.details as Record<string, string> | null;

  switch (action.type) {
    case "Sown":
      return `Sowed ${quantity} ${plant}${where}`;
    case "Received":
      return `Received ${quantity} ${plant}${where}`;
    case "CuttingsTaken":
      return `Took ${quantity} cuttings of ${plant}${where}`;
    case "Lost":
      return `Lost ${quantity} ${plant} (${lossLabel(details?.reason)})`;
    case "Moved":
      return `Moved ${action.labelCode}${where}`;
    case "PottedOn":
      return `Potted on ${quantity} ${plant}`;
    case "PrickedOut":
      return `Pricked out ${quantity} ${plant}`;
    case "StockCounted":
      return `Counted ${action.labelCode}`;
    case "Germinated":
      return `${action.labelCode} germinated`;
    case "Treated":
      return `Treated ${action.labelCode} with ${details?.product}`;
    case "Note":
      return `Note on ${action.labelCode}`;
    default:
      return String(action.type);
  }
}

/** The same work again, as new work, if it makes sense to repeat with one press. */
export function repeatOf(action: Schemas["RecentAction"]): WorkRequest | null {
  const line = action.lines[0];
  const details = action.details as Record<string, string> | null;

  if (
    startTypes.includes(action.type as string) &&
    line &&
    action.containerKind
  ) {
    return work.startBatch({
      plantId: line.plantId,
      quantity: line.quantityChange,
      source: line.source,
      sourceReference: line.sourceReference,
      supplierId: line.supplierId,
      parentBatchId: null,
      motherPlant: null,
      originCountry: line.originCountry,
      containerKind: action.containerKind,
      locationId: action.locationId,
      potSizeId: action.potSizeId,
    });
  }

  if (action.type === "Lost" && line && action.containerId && details) {
    return work.lose(action.containerId, {
      batchId: line.batchId,
      quantity: Math.abs(Number(line.quantityChange)),
      reason: details.reason as Schemas["LossReason"],
    });
  }

  if (action.type === "Treated" && action.containerId && details) {
    return work.treat(action.containerId, {
      product: details.product,
      dose: details.dose,
      area: details.area,
    });
  }

  return null;
}

/** A place's name with what it's inside, skipping sites, which rarely help. */
export function placeLabel(
  place: Schemas["LocationResponse"],
  all: Schemas["LocationResponse"][],
): string {
  const parent = all.find((item) => item.id === place.parentId);
  return parent && parent.kind !== "Site"
    ? `${parent.name}, ${place.name}`
    : place.name;
}

/** Places a container can actually stand in. */
export function workPlaces(all: Schemas["LocationResponse"][]) {
  return all
    .filter((place) => place.kind !== "Site")
    .map((place) => ({ id: place.id, label: placeLabel(place, all) }));
}
