import Cylinder from "@lucide/svelte/icons/cylinder";
import DatabaseBackup from "@lucide/svelte/icons/database-backup";
import Info from "@lucide/svelte/icons/info";
import Printer from "@lucide/svelte/icons/printer";
import KeyRound from "@lucide/svelte/icons/key-round";
import MapIcon from "@lucide/svelte/icons/map";
import Store from "@lucide/svelte/icons/store";
import Tablet from "@lucide/svelte/icons/tablet";
import Tag from "@lucide/svelte/icons/tag";
import Thermometer from "@lucide/svelte/icons/thermometer";
import ToggleRight from "@lucide/svelte/icons/toggle-right";
import UserRound from "@lucide/svelte/icons/user-round";
import Users from "@lucide/svelte/icons/users";
import { isOn, type Feature } from "./features.svelte";

// What's set once and then left alone, kept out of the everyday sidebar.
export type SettingsPage =
  | "account"
  | "nursery"
  | "features"
  | "map"
  | "sensors"
  | "printers"
  | "labels"
  | "potSizes"
  | "staff"
  | "signingIn"
  | "stations"
  | "data"
  | "about";

export type SettingsItem = {
  page: SettingsPage;
  label: string;
  icon: typeof Info;
  /** Shown only while this part of GrowLog is on. */
  feature?: Feature;
};

type SettingsGroup = {
  heading: string;
  ownerOnly: boolean;
  items: SettingsItem[];
};

const groups: SettingsGroup[] = [
  {
    heading: "You",
    ownerOnly: false,
    items: [{ page: "account", label: "My account", icon: UserRound }],
  },
  {
    heading: "The nursery",
    ownerOnly: true,
    items: [
      { page: "nursery", label: "Nursery details", icon: Store },
      { page: "features", label: "Features", icon: ToggleRight },
      { page: "map", label: "Map", icon: MapIcon, feature: "PlacesMap" },
      { page: "potSizes", label: "Pot sizes", icon: Cylinder },
      {
        page: "sensors",
        label: "Sensors",
        icon: Thermometer,
        feature: "Climate",
      },
    ],
  },
  {
    heading: "Printing",
    ownerOnly: true,
    items: [
      { page: "printers", label: "Printers", icon: Printer },
      { page: "labels", label: "Labels", icon: Tag },
    ],
  },
  {
    heading: "People",
    ownerOnly: true,
    items: [
      { page: "staff", label: "Staff", icon: Users },
      { page: "signingIn", label: "Signing in", icon: KeyRound },
      { page: "stations", label: "Bench stations", icon: Tablet },
    ],
  },
  {
    heading: "Records",
    ownerOnly: true,
    items: [{ page: "data", label: "Data and backups", icon: DatabaseBackup }],
  },
  {
    heading: "GrowLog",
    ownerOnly: false,
    items: [{ page: "about", label: "About GrowLog", icon: Info }],
  },
];

/** The settings someone can see: staff get their own account and About; owners get everything that's turned on. */
export const settingsGroups = (isOwner: boolean) =>
  groups
    .filter((group) => isOwner || !group.ownerOnly)
    .map((group) => ({
      ...group,
      items: group.items.filter((item) => !item.feature || isOn(item.feature)),
    }));

export const canOpen = (page: SettingsPage, isOwner: boolean) =>
  settingsGroups(isOwner).some((group) =>
    group.items.some((item) => item.page === page),
  );

// Screens that used to be under Manage, so a reload or Back to one still lands in the right place.
const moved: Record<string, SettingsPage> = {
  nursery: "nursery",
  potSizes: "potSizes",
  staff: "staff",
  stations: "stations",
  signIn: "signingIn",
};

export const movedToSettings = (section: unknown): SettingsPage | null =>
  typeof section === "string" ? (moved[section] ?? null) : null;
