import { stageLabel } from "./production";
import type { Schemas } from "./types";

export type BatchPhotoKind = Schemas["BatchPhotoKind"];

export const photoKinds: { kind: BatchPhotoKind; label: string }[] = [
  { kind: "Growing", label: "The plants" },
  { kind: "Packet", label: "Seed packet" },
  { kind: "Label", label: "Label" },
  { kind: "Packaging", label: "Packaging" },
];

/** Under each photo in the strip: the day of the batch and its stage, or what the photo is of. */
export function photoCaption(photo: {
  kind: BatchPhotoKind;
  day?: number | string | null;
  stage?: Schemas["ProductionStage"] | null;
}): string {
  const day = photo.day == null ? "" : `Day ${photo.day}`;
  const what =
    photo.kind === "Growing"
      ? photo.stage
        ? stageLabel(photo.stage)
        : ""
      : (photoKinds.find((item) => item.kind === photo.kind)?.label ?? "");

  return [day, what].filter(Boolean).join(" · ") || "Photo";
}
