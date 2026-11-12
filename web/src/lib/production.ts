import type { Schemas } from "./types";

export type ProductionRow = Schemas["ProductionRow"];
export type Stage = Schemas["ProductionStage"];

export const stages: { stage: Stage; label: string; always: boolean }[] = [
  { stage: "Sown", label: "Sown", always: true },
  { stage: "Cuttings", label: "Cuttings", always: false },
  { stage: "BoughtIn", label: "Bought in", always: false },
  { stage: "Germinated", label: "Germinated", always: true },
  { stage: "PrickedOut", label: "Pricked out", always: true },
  { stage: "PottedOn", label: "Potted on", always: true },
  { stage: "Ready", label: "Ready", always: true },
];

export function stageLabel(stage: Stage | null | undefined): string {
  return stages.find((item) => item.stage === stage)?.label ?? "Not recorded";
}

export interface ProductionFilters {
  text: string;
  place: string;
  supplier: string;
}

/** Everything the search box looks through. */
function searchable(row: ProductionRow): string {
  return [
    row.commonName,
    row.variety,
    row.botanicalName,
    row.labelCode,
    row.traceabilityCode,
  ]
    .filter(Boolean)
    .join(" ")
    .toLowerCase();
}

export function filterRows(
  rows: ProductionRow[],
  filters: ProductionFilters,
): ProductionRow[] {
  const text = filters.text.trim().toLowerCase();

  return rows.filter(
    (row) =>
      (!text || searchable(row).includes(text)) &&
      (!filters.place || row.locationName === filters.place) &&
      (!filters.supplier || row.supplierName === filters.supplier),
  );
}

/** How many plants are at each stage. */
export function plantsByStage(rows: ProductionRow[]): Map<Stage, number> {
  const counts = new Map<Stage, number>();

  for (const row of rows) {
    if (row.stage) {
      counts.set(
        row.stage,
        (counts.get(row.stage) ?? 0) + Number(row.quantity),
      );
    }
  }

  return counts;
}

export function totalPlants(rows: ProductionRow[]): number {
  return rows.reduce((total, row) => total + Number(row.quantity), 0);
}

/** Trays nobody has counted yet, in all and at each stage: their plants aren't in the totals, so they're counted apart. */
export function uncountedTrays(rows: ProductionRow[]): {
  all: number;
  byStage: Map<Stage, number>;
} {
  const byStage = new Map<Stage, number>();
  const uncounted = rows.filter((row) => row.notCounted);

  for (const row of uncounted) {
    if (row.stage) {
      byStage.set(row.stage, (byStage.get(row.stage) ?? 0) + 1);
    }
  }

  return { all: uncounted.length, byStage };
}

/** A stage's total for its chip: the plants counted, and any trays not counted yet. */
export function chipTotal(plants: number, uncounted: number): string {
  if (uncounted === 0) {
    return String(plants);
  }

  const trays = `${uncounted} uncounted`;
  return plants > 0 ? `${plants} + ${trays}` : trays;
}

/** Longest at their stage first, since those are likely to need doing next. */
export function oldestFirst(rows: ProductionRow[]): ProductionRow[] {
  return [...rows].sort((a, b) =>
    (a.stageSince ?? "").localeCompare(b.stageSince ?? ""),
  );
}

export function timeAtStage(
  since: string | null | undefined,
  now: Date,
): string {
  if (!since) {
    return "";
  }

  const days = Math.floor(
    (now.getTime() - new Date(since).getTime()) / 86_400_000,
  );

  return days < 1 ? "today" : days === 1 ? "1 day" : `${days} days`;
}

/** The distinct values of one column, for a drop-down. */
export function choicesOf(values: (string | null | undefined)[]): string[] {
  return [...new Set(values.filter((value) => !!value) as string[])].sort(
    (a, b) => a.localeCompare(b),
  );
}
