import { useState } from "react";
import s from "./DriverPhoto.module.css";

/**
 * The feed hands out the 93x93 "1col" crop, which is unusable above avatar
 * size. The same URL with a larger transform is served straight from F1's
 * media pipeline: 4col is 432px, 6col is 658px.
 *
 * Only the SIZE is swapped — the rest of the path comes from the feed. A URL
 * rebuilt from the driver reference instead resolves to F1's 700-byte fallback
 * image, which is how this was first got wrong.
 */
function atSize(url: string, pixels: number): string {
  const transform = pixels > 480 ? "6col" : pixels > 200 ? "4col" : "2col";
  return url.replace(/\.transform\/[^/]+\/image\.png/, `.transform/${transform}/image.png`);
}

export interface DriverPhotoProps {
  url: string | null | undefined;
  tla: string;
  /** Team colour, used for the placeholder and the ring. */
  colour: string;
  size?: number;
  round?: boolean;
  /**
   * Render as a transparent cut-out rather than a filled avatar: no plate
   * behind the driver, and the image is not cropped to the frame.
   */
  cutout?: boolean;
}

/**
 * A driver's photograph, with the initials behind it.
 *
 * The placeholder is not decoration: it holds the exact final size, so a row of
 * twenty photographs arriving one at a time does not reflow the page. A failed
 * or blocked image simply leaves the initials, which is a complete state rather
 * than a gap.
 */
export function DriverPhoto({
  url, tla, colour, size = 34, round = true, cutout = false,
}: DriverPhotoProps) {
  const [failed, setFailed] = useState(false);
  const [loaded, setLoaded] = useState(false);

  const show = Boolean(url) && !failed;

  return (
    <div
      className={`${s.wrap} ${cutout ? s.cutout : ""}`}
      style={{
        width: size,
        height: size,
        borderRadius: round && !cutout ? "50%" : 0,
        background: cutout ? "transparent" : colour,
      }}
    >
      {show && (
        <img
          className={`${s.photo} ${loaded ? "" : s.hidden}`}
          src={atSize(url!, size)}
          alt=""
          loading="lazy"
          decoding="async"
          referrerPolicy="no-referrer"
          onLoad={() => setLoaded(true)}
          onError={() => setFailed(true)}
        />
      )}
      {(!show || !loaded) && (
        <span className={s.fallback}
              style={{ fontSize: size * 0.4, color: cutout ? colour : "var(--bg)" }}>
          {tla}
        </span>
      )}
    </div>
  );
}
