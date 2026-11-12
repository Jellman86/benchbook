import { originCountries } from "./countries";
import { sowByWords } from "./sowBy";
import { dayLabel } from "./dates";
import type { Schemas } from "./types";
import { plantName } from "./work";

export interface BatchForm {
  plantId: string;
  source: Schemas["BatchSource"];
  sourceReference: string;
  supplierId: string;
  motherPlant: string;
  originCountry: string;
  /** The day it was sown, arrived or had cuttings taken, or empty if that isn't recorded. */
  startedOn: string;
  quantity: number;
  /** A seed packet's sow-by date, or empty. Only seed keeps one. */
  sowBy: string;
}

export const sourceWords: Record<Schemas["BatchSource"], string> = {
  SeedLot: "Seed",
  BoughtIn: "Bought in",
  Cuttings: "Cuttings",
};

/** What an edit changes, in words, so it can be checked before it's saved. */
export function batchChanges(
  before: BatchForm,
  after: BatchForm,
  plants: Pick<Schemas["PlantResponse"], "id" | "commonName" | "variety">[],
  suppliers: Pick<Schemas["SupplierResponse"], "id" | "name">[],
): { name: string; before: string; after: string }[] {
  const plant = (id: string) => {
    const found = plants.find((item) => item.id === id);
    return found ? plantName(found.commonName, found.variety) : "";
  };
  const supplier = (form: BatchForm) =>
    form.source === "Cuttings"
      ? ""
      : (suppliers.find((item) => item.id === form.supplierId)?.name ?? "");
  const country = (code: string) => {
    const clean = code.trim().toUpperCase();
    return originCountries.find((item) => item.code === clean)?.name ?? clean;
  };
  const text = (value: string) => value.trim();
  // As it will be saved: cuttings have a mother plant, everything else a lot or reference.
  // What is held now is shown as it is, so a clearing is always listed.
  const reference = (form: BatchForm) =>
    form.source === "Cuttings" ? "" : text(form.sourceReference);
  const mother = (form: BatchForm) =>
    form.source === "Cuttings" ? text(form.motherPlant) : "";
  const day = (value: string) => (value ? dayLabel(value) : "");

  return [
    {
      name: "Plant",
      before: plant(before.plantId),
      after: plant(after.plantId),
    },
    {
      name: "Source",
      before: sourceWords[before.source],
      after: sourceWords[after.source],
    },
    {
      name: "Started",
      before: day(before.startedOn),
      after: day(after.startedOn),
    },
    {
      name: "How many",
      before: String(before.quantity),
      after: String(after.quantity),
    },
    { name: "Supplier", before: supplier(before), after: supplier(after) },
    {
      name: "Lot or reference",
      before: text(before.sourceReference),
      after: reference(after),
    },
    {
      name: "Mother plant",
      before: text(before.motherPlant),
      after: mother(after),
    },
    {
      name: "Sow by",
      before: sowByWords(before.sowBy),
      after: after.source === "SeedLot" ? sowByWords(after.sowBy) : "",
    },
    {
      name: "Country of origin",
      before: country(before.originCountry),
      after: country(after.originCountry),
    },
  ].filter((change) => change.before !== change.after);
}
