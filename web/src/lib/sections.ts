import ChartColumn from "@lucide/svelte/icons/chart-column";
import ClipboardList from "@lucide/svelte/icons/clipboard-list";
import Contact from "@lucide/svelte/icons/contact";
import FileText from "@lucide/svelte/icons/file-text";
import Flower2 from "@lucide/svelte/icons/flower-2";
import MapPin from "@lucide/svelte/icons/map-pin";
import ReceiptText from "@lucide/svelte/icons/receipt-text";
import Package from "@lucide/svelte/icons/package";
import Truck from "@lucide/svelte/icons/truck";
import RouteIcon from "@lucide/svelte/icons/route";
import Sprout from "@lucide/svelte/icons/sprout";
import Thermometer from "@lucide/svelte/icons/thermometer";
import Wallet from "@lucide/svelte/icons/wallet";
import { anyOn, type Feature } from "./features.svelte";

// The owner's everyday work. Setting the nursery up lives under Settings (settings.ts).
export type ManageSection =
  | "plants"
  | "places"
  | "trace"
  | "jobs"
  | "mothers"
  | "climate"
  | "figures"
  | "money"
  | "invoices"
  | "consignments"
  | "customers"
  | "purchases"
  | "suppliers";

export type ManageItem = {
  section: ManageSection;
  label: string;
  icon: typeof Flower2;
  /** Shown while any of these parts of GrowLog is on; always shown without. */
  features?: Feature[];
};

export const manageGroups: { heading: string; items: ManageItem[] }[] = [
  {
    heading: "Growing",
    items: [
      { section: "plants", label: "Plants", icon: Flower2 },
      { section: "places", label: "Places", icon: MapPin },
      { section: "trace", label: "Trace a batch", icon: RouteIcon },
      {
        section: "mothers",
        label: "Mother plants",
        icon: Sprout,
        features: ["MotherPlants"],
      },
      {
        section: "jobs",
        label: "Jobs",
        icon: ClipboardList,
        features: ["Jobs"],
      },
      {
        section: "climate",
        label: "Climate",
        icon: Thermometer,
        features: ["Climate"],
      },
    ],
  },
  {
    heading: "Business",
    items: [
      { section: "figures", label: "Figures", icon: ChartColumn },
      { section: "money", label: "Money", icon: Wallet },
      {
        section: "invoices",
        label: "Invoices",
        icon: FileText,
        features: ["Invoices"],
      },
      {
        section: "consignments",
        label: "Consignments",
        icon: Package,
        features: ["Passports"],
      },
      {
        section: "customers",
        label: "Customers",
        icon: Contact,
        features: ["Invoices", "Passports"],
      },
      {
        section: "purchases",
        label: "Purchases",
        icon: ReceiptText,
        features: ["Purchases"],
      },
      { section: "suppliers", label: "Suppliers", icon: Truck },
    ],
  },
];

export const manageSections = manageGroups.flatMap((group) => group.items);

const shown = (item: ManageItem) => !item.features || anyOn(...item.features);

/** The owner's sections, less any whose part of GrowLog is turned off. */
export const visibleManageGroups = () =>
  manageGroups
    .map((group) => ({ ...group, items: group.items.filter(shown) }))
    .filter((group) => group.items.length > 0);

/** Whether a section can be opened, so a remembered or linked one that's been turned off isn't. */
export const isManageSectionOn = (section: ManageSection) =>
  manageSections.some((item) => item.section === section && shown(item));

export const isManageSection = (value: unknown): value is ManageSection =>
  manageSections.some((item) => item.section === value);

/** A section's name, for its breadcrumb. */
export const sectionLabel = (section: ManageSection) =>
  manageSections.find((item) => item.section === section)?.label ?? "Manage";
