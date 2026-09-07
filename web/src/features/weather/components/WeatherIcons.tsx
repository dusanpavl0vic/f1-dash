/**
 * Weather glyphs, drawn inline rather than pulled from an icon font.
 *
 * They inherit currentColor and scale with the value they sit beside, and
 * shipping five paths beats a webfont request for five glyphs.
 */

const stroke = {
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 1.6,
  strokeLinecap: "round" as const,
  strokeLinejoin: "round" as const,
};

export function TrackTempIcon({ size = 26 }: { size?: number }) {
  // A thermometer over a strip of asphalt.
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true">
      <path {...stroke} d="M10 14.5V4.5a2 2 0 1 1 4 0v10a3.5 3.5 0 1 1-4 0Z" />
      <circle cx="12" cy="17.5" r="1.6" fill="currentColor" />
      <path {...stroke} d="M4 21h4M16 21h4" opacity="0.5" />
    </svg>
  );
}

export function AirTempIcon({ size = 26 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true">
      <circle {...stroke} cx="12" cy="12" r="4" />
      <path {...stroke} d="M12 2v2.5M12 19.5V22M2 12h2.5M19.5 12H22M4.9 4.9l1.8 1.8M17.3 17.3l1.8 1.8M19.1 4.9l-1.8 1.8M6.7 17.3l-1.8 1.8" />
    </svg>
  );
}

export function HumidityIcon({ size = 20 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true">
      <path {...stroke} d="M12 3.5s5.5 6.1 5.5 9.6a5.5 5.5 0 1 1-11 0C6.5 9.6 12 3.5 12 3.5Z" />
    </svg>
  );
}

export function PressureIcon({ size = 20 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true">
      <circle {...stroke} cx="12" cy="12" r="8.5" />
      <path {...stroke} d="M12 12l4-3.5" />
      <circle cx="12" cy="12" r="1.3" fill="currentColor" />
    </svg>
  );
}

/**
 * Wind, with the barb rotated to the reported bearing.
 *
 * The feed gives WindDirection as degrees the wind blows FROM, which is the
 * meteorological convention, so the arrow is rotated 180 degrees to point the
 * way the air is actually moving.
 */
export function WindIcon({ size = 20, degrees = 0 }: { size?: number; degrees?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true">
      <g transform={`rotate(${degrees + 180} 12 12)`}>
        <path {...stroke} d="M12 4.5v15M12 4.5l-4 4.5M12 4.5l4 4.5" />
      </g>
    </svg>
  );
}

export function RainIcon({ size = 20, raining = false }: { size?: number; raining?: boolean }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true">
      <path {...stroke} d="M7 15.5a4 4 0 0 1 .4-8 5.5 5.5 0 0 1 10.5 1.6A3.4 3.4 0 0 1 17.5 15.5Z" />
      {raining && <path {...stroke} d="M8.5 18l-1 2.5M12 18l-1 2.5M15.5 18l-1 2.5" />}
    </svg>
  );
}
