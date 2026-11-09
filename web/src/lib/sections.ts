import ChartColumn from "@lucide/svelte/icons/chart-column";
import Cylinder from "@lucide/svelte/icons/cylinder";
import Flower2 from "@lucide/svelte/icons/flower-2";
import MapPin from "@lucide/svelte/icons/map-pin";
import ReceiptText from "@lucide/svelte/icons/receipt-text";
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
  | "figures"
  | "purchases"
  | "money";

export const manageSections: {
  section: ManageSection;
  label: string;
  icon: typeof Flower2;
}[] = [
  { section: "figures", label: "Figures", icon: ChartColumn },
  { section: "money", label: "Money", icon: Wallet },
  { section: "purchases", label: "Purchases", icon: ReceiptText },
  { section: "plants", label: "Plants", icon: Flower2 },
  { section: "potSizes", label: "Pot sizes", icon: Cylinder },
  { section: "suppliers", label: "Suppliers", icon: Truck },
  { section: "places", label: "Places", icon: MapPin },
  { section: "staff", label: "Staff", icon: Users },
  { section: "stations", label: "Bench stations", icon: Tablet },
];
