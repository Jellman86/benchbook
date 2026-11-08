export type ManageSection =
  | "staff"
  | "plants"
  | "potSizes"
  | "suppliers"
  | "places"
  | "stations"
  | "figures"
  | "purchases"
  | "money";

export const manageSections: { section: ManageSection; label: string }[] = [
  { section: "figures", label: "Figures" },
  { section: "money", label: "Money" },
  { section: "purchases", label: "Purchases" },
  { section: "plants", label: "Plants" },
  { section: "potSizes", label: "Pot sizes" },
  { section: "suppliers", label: "Suppliers" },
  { section: "places", label: "Places" },
  { section: "staff", label: "Staff" },
  { section: "stations", label: "Bench stations" },
];
