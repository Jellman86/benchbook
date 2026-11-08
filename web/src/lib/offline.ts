import type { Me } from "./boot";
import type { Schemas } from "./types";

const keys = {
  me: "growlog.me",
  containers: "growlog.containers",
  places: "growlog.places",
};

function read<T>(key: string): T | null {
  const value = localStorage.getItem(key);
  return value === null ? null : (JSON.parse(value) as T);
}

export function rememberMe(me: Me | null) {
  if (me) {
    localStorage.setItem(keys.me, JSON.stringify(me));
  } else {
    localStorage.removeItem(keys.me);
  }
}

export const rememberedMe = () => read<Me>(keys.me);

export function rememberContainers(containers: Schemas["ContainerResponse"][]) {
  localStorage.setItem(keys.containers, JSON.stringify(containers));
}

export function rememberedContainer(label: string) {
  return (
    read<Schemas["ContainerResponse"][]>(keys.containers)?.find(
      (container) =>
        container.labelCode.toLowerCase() === label.trim().toLowerCase(),
    ) ?? null
  );
}

export function rememberPlaces(places: Schemas["LocationResponse"][]) {
  localStorage.setItem(keys.places, JSON.stringify(places));
}

export const rememberedPlaces = () =>
  read<Schemas["LocationResponse"][]>(keys.places) ?? [];
