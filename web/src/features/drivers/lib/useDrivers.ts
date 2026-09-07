import { useEffect, useMemo, useState } from "react";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";

export interface DriverProfile {
  racingNumber: string;
  tla: string;
  fullName: string;
  firstName: string;
  lastName: string;
  teamName: string;
  /** Hex without a leading '#'. */
  teamColour: string;
  countryCode: string;
  /**
   * The feed's own URL. Only this exact string resolves to a photograph — a
   * reconstructed one returns F1's 700-byte fallback, which is why it is never
   * rebuilt from the driver reference.
   */
  headshotUrl: string | null;
  reference: string;
}

/**
 * Driver profiles for a season, keyed by TLA.
 *
 * No module cache: the endpoint is served with a day-long Cache-Control, so a
 * refetch on remount is a browser-cache hit and costs nothing. A module cache
 * here would only exist to save that hit, and it forced state to be set
 * synchronously inside an effect — a cascading render traded for nothing.
 *
 * The result is stored WITH the year it belongs to, so a year change is handled
 * by derivation rather than by clearing state.
 */
export function useDrivers(year: number | null): Map<string, DriverProfile> {
  const [loaded, setLoaded] = useState<{ year: number; profiles: DriverProfile[] } | null>(null);

  useEffect(() => {
    if (year === null) return;

    let cancelled = false;
    void fetch(`${API_URL}/api/drivers/${year}`)
      .then((r) => (r.ok ? (r.json() as Promise<DriverProfile[]>) : []))
      .then((profiles) => { if (!cancelled) setLoaded({ year, profiles }); })
      .catch(() => undefined);

    return () => { cancelled = true; };
  }, [year]);

  return useMemo(() => {
    const profiles = loaded?.year === year ? loaded.profiles : [];
    return new Map(profiles.map((p) => [p.tla, p]));
  }, [loaded, year]);
}
