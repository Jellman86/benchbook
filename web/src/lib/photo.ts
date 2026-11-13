/** Phone photos are huge. A packet or label stays readable at this size. */
const longestSide = 1600;

function loadImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () =>
      reject(new Error("Not a picture this browser can read."));
    image.src = url;
  });
}

/** Scales a photo down for upload. If the browser can't, the server still checks the original. */
export async function shrinkPhoto(
  file: File,
  longest = longestSide,
): Promise<Blob> {
  const url = URL.createObjectURL(file);

  try {
    const image = await loadImage(url);
    const scale = Math.min(
      1,
      longest / Math.max(image.naturalWidth, image.naturalHeight),
    );
    const canvas = document.createElement("canvas");
    canvas.width = Math.round(image.naturalWidth * scale);
    canvas.height = Math.round(image.naturalHeight * scale);
    canvas
      .getContext("2d")
      ?.drawImage(image, 0, 0, canvas.width, canvas.height);

    const shrunk = await new Promise<Blob | null>((resolve) =>
      canvas.toBlob(resolve, "image/jpeg", 0.82),
    );

    return shrunk ?? file;
  } catch {
    return file;
  } finally {
    URL.revokeObjectURL(url);
  }
}
