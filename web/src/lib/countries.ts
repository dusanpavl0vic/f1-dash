/**
 * Country name to flag.
 *
 * Emoji flags rather than image assets: no request, no licensing question, no
 * sprite to keep in sync with a changing calendar, and they inherit the text
 * size they sit in. The names are exactly the strings Jolpica returns — it uses
 * "UK", "USA" and "UAE" rather than the official long forms.
 */
const ISO: Record<string, string> = {
  Australia: "AU", Austria: "AT", Azerbaijan: "AZ", Bahrain: "BH", Belgium: "BE",
  Brazil: "BR", Canada: "CA", China: "CN", France: "FR", Germany: "DE",
  Hungary: "HU", India: "IN", Italy: "IT", Japan: "JP", Korea: "KR",
  Malaysia: "MY", Mexico: "MX", Monaco: "MC", Morocco: "MA", Netherlands: "NL",
  Portugal: "PT", Qatar: "QA", Russia: "RU", "Saudi Arabia": "SA", Singapore: "SG",
  "South Africa": "ZA", Spain: "ES", Sweden: "SE", Switzerland: "CH", Turkey: "TR",
  UAE: "AE", UK: "GB", USA: "US", Vietnam: "VN",

  // Nationalities, as Ergast reports them for drivers and constructors.
  Australian: "AU", Austrian: "AT", Belgian: "BE", Brazilian: "BR", British: "GB",
  Canadian: "CA", Chinese: "CN", Danish: "DK", Dutch: "NL", Finnish: "FI",
  French: "FR", German: "DE", Italian: "IT", Japanese: "JP", Mexican: "MX",
  Monegasque: "MC", "New Zealander": "NZ", Polish: "PL", Russian: "RU",
  Spanish: "ES", Swiss: "CH", Thai: "TH", American: "US",
};

/** Regional indicator symbols: 'A' is U+1F1E6. */
export function flag(country: string | null | undefined): string {
  if (!country) return "";

  const code = ISO[country.trim()];
  if (!code) return "";

  return String.fromCodePoint(...[...code].map((c) => 0x1f1e6 + c.charCodeAt(0) - 65));
}
