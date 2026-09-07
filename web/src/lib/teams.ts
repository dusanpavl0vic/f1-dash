/**
 * Constructor name to colour.
 *
 * The live dashboard takes team colours from the feed at runtime and must never
 * hardcode them. This map is for the pages that have NO feed — schedule and
 * standings come from Jolpica, which supplies a constructor name and nothing
 * else — so a lookup is the only option there.
 *
 * Names are matched loosely because Jolpica is inconsistent across seasons:
 * "Red Bull" and "Red Bull Racing", "RB F1 Team" and "Racing Bulls", "Alfa
 * Romeo" becoming "Sauber" becoming "Kick Sauber" becoming "Audi".
 */
const COLOURS: [RegExp, string][] = [
  [/red\s*bull(?!.*\b(rb|racing bulls)\b)/i, "#3671C6"],
  [/mercedes/i, "#27F4D2"],
  [/ferrari/i, "#E80020"],
  [/mclaren/i, "#FF8000"],
  [/aston\s*martin/i, "#229971"],
  [/alpine/i, "#0093CC"],
  [/williams/i, "#64C4FF"],
  [/(racing\s*bulls|^rb\b|rb f1|alphatauri|toro rosso)/i, "#6692FF"],
  [/audi/i, "#00505C"],
  [/cadillac/i, "#B08D57"],
  [/(kick\s*sauber|sauber|alfa\s*romeo)/i, "#52E252"],
  [/haas/i, "#B6BABD"],
  [/renault/i, "#FFF500"],
  [/racing\s*point|force\s*india/i, "#F596C8"],
];

export function teamColour(constructor: string | null | undefined): string {
  if (!constructor) return "var(--neutral)";

  for (const [pattern, colour] of COLOURS) {
    if (pattern.test(constructor)) return colour;
  }
  return "var(--neutral)";
}
