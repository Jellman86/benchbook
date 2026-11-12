import ChartColumn from "@lucide/svelte/icons/chart-column";
import ClipboardList from "@lucide/svelte/icons/clipboard-list";
import Contact from "@lucide/svelte/icons/contact";
import FileText from "@lucide/svelte/icons/file-text";
import Flower2 from "@lucide/svelte/icons/flower-2";
import MapPin from "@lucide/svelte/icons/map-pin";
import ReceiptText from "@lucide/svelte/icons/receipt-text";
import Truck from "@lucide/svelte/icons/truck";
import RouteIcon from "@lucide/svelte/icons/route";
import Wallet from "@lucide/svelte/icons/wallet";

// The owner's everyday work. Setting the nursery up lives under Settings (settings.ts).
export type ManageSection =
  | "plants"
  | "places"
  | "trace"
  | "jobs"
  | "figures"
  | "money"
  | "invoices"
  | "customers"
  | "purchases"
  | "suppliers";

export type ManageItem = {
  section: ManageSection;
  label: string;
  icon: typeof Flower2;
};

export const manageGroups: { heading: string; items: ManageItem[] }[] = [
  {
    heading: "Growing",
    items: [
      { section: "plants", label: "Plants", icon: Flower2 },
      { section: "places", label: "Places", icon: MapPin },
      { section: "trace", label: "Trace a batch", icon: RouteIcon },
      { section: "jobs", label: "Jobs", icon: ClipboardList },
    ],
  },
  {
    heading: "Business",
    items: [
      { section: "figures", label: "Figures", icon: ChartColumn },
      { section: "money", label: "Money", icon: Wallet },
      { section: "invoices", label: "Invoices", icon: FileText },
      { section: "customers", label: "Customers", icon: Contact },
      { section: "purchases", label: "Purchases", icon: ReceiptText },
      { section: "suppliers", label: "Suppliers", icon: Truck },
    ],
  },
];

export const manageSections = manageGroups.flatMap((group) => group.items);

export const isManageSection = (value: unknown): value is ManageSection =>
  manageSections.some((item) => item.section === value);
