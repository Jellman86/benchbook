import ChartColumn from "@lucide/svelte/icons/chart-column";
import Cylinder from "@lucide/svelte/icons/cylinder";
import Flower2 from "@lucide/svelte/icons/flower-2";
import KeyRound from "@lucide/svelte/icons/key-round";
import MapPin from "@lucide/svelte/icons/map-pin";
import ReceiptText from "@lucide/svelte/icons/receipt-text";
import Store from "@lucide/svelte/icons/store";
import FileText from "@lucide/svelte/icons/file-text";
import Contact from "@lucide/svelte/icons/contact";
import Tablet from "@lucide/svelte/icons/tablet";
import Truck from "@lucide/svelte/icons/truck";
import Users from "@lucide/svelte/icons/users";
import Wallet from "@lucide/svelte/icons/wallet";

export type ManageSection =
  | "staff"
  | "plants"
  | "potSizes"
  | "suppliers"
  | "places"
  | "stations"
  | "signIn"
  | "figures"
  | "purchases"
  | "money"
  | "nursery"
  | "invoices"
  | "customers";

export const manageSections: {
  section: ManageSection;
  label: string;
  icon: typeof Flower2;
}[] = [
  { section: "figures", label: "Figures", icon: ChartColumn },
  { section: "money", label: "Money", icon: Wallet },
  { section: "invoices", label: "Invoices", icon: FileText },
  { section: "customers", label: "Customers", icon: Contact },
  { section: "purchases", label: "Purchases", icon: ReceiptText },
  { section: "plants", label: "Plants", icon: Flower2 },
  { section: "potSizes", label: "Pot sizes", icon: Cylinder },
  { section: "suppliers", label: "Suppliers", icon: Truck },
  { section: "places", label: "Places", icon: MapPin },
  { section: "staff", label: "Staff", icon: Users },
  { section: "stations", label: "Bench stations", icon: Tablet },
  { section: "signIn", label: "Staff login", icon: KeyRound },
  { section: "nursery", label: "Nursery and labels", icon: Store },
];
