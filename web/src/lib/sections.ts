export type ManageSection =
  | "staff"
  | "plants"
  | "potSizes"
  | "suppliers"
  | "places"
  | "stations"
  | "figures";

export const manageSections: { section: ManageSection; label: string }[] = [
  { section: "figures", label: "Figures" },
  { section: "plants", label: "Plants" },
  { section: "potSizes", label: "Pot sizes" },
  { section: "suppliers", label: "Suppliers" },
  { section: "places", label: "Places" },
  { section: "staff", label: "Staff" },
  { section: "stations", label: "Bench stations" },
];
