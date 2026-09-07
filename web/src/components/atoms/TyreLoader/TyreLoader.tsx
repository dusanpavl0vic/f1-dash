import tyre from "@/assets/f1-tyre-soft.svg";
import s from "./TyreLoader.module.css";

export type TyreLoaderSize = "sm" | "md" | "lg";

const SIZE_PX: Record<TyreLoaderSize, number> = { sm: 16, md: 34, lg: 64 };

export interface TyreLoaderProps {
  /** What is being waited for. Always say it — a bare spinner tells nobody anything. */
  label?: string;
  /** Second line, for a wait the user should understand is long. */
  detail?: string | undefined;
  size?: TyreLoaderSize;
  /** Lay the label beside the tyre instead of beneath it. */
  inline?: boolean;
  /** Add generous vertical padding, for a whole empty panel. */
  block?: boolean;
}

/**
 * The application's loading indicator: a spinning F1 tyre.
 *
 * It is `role="status"` with `aria-live="polite"` so a screen reader announces
 * the wait rather than sitting silently through it, and the visible label is
 * the accessible name — the reason every caller passes one.
 */
export function TyreLoader({
  label = "Loading", detail, size = "md", inline = false, block = false,
}: TyreLoaderProps) {
  const px = SIZE_PX[size];

  return (
    <div
      className={`${s.wrap} ${inline ? s.inline : ""} ${block ? s.block : ""}`}
      role="status"
      aria-live="polite"
    >
      <img
        className={s.tyre}
        src={tyre}
        width={px}
        height={px}
        alt=""
        aria-hidden="true"
        draggable={false}
      />
      <div>
        <div className={`${s.label} ${inline ? s.labelInline : ""}`}>{label}</div>
        {detail && <div className={s.detail}>{detail}</div>}
      </div>
    </div>
  );
}
