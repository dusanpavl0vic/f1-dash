import { memo } from "react";
import type { Weather } from "@/features/live/model/types";
import {
  AirTempIcon, HumidityIcon, PressureIcon, RainIcon, TrackTempIcon, WindIcon,
} from "./WeatherIcons";
import s from "./WeatherPanel.module.css";

/**
 * Track temperature ranges seen in F1: roughly 15°C on a cold wet morning to
 * 60°C in Bahrain or Qatar. The bar is scaled to that, not to an arbitrary 0-100.
 */
const TRACK_MIN = 15;
const TRACK_MAX = 60;
const AIR_MIN = 5;
const AIR_MAX = 45;

function tempColor(celsius: number, min: number, max: number): string {
  const t = Math.min(1, Math.max(0, (celsius - min) / (max - min)));
  if (t < 0.33) return "var(--blue-light)";
  if (t < 0.66) return "var(--green)";
  if (t < 0.85) return "var(--orange)";
  return "var(--red)";
}

/** A compass point reads faster than a bearing in degrees. */
function compass(degrees: number): string {
  const points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
  return points[Math.round(degrees / 45) % 8]!;
}

function condition(weather: Weather): { label: string; color: string } {
  if (weather.rainfall) return { label: "Wet", color: "var(--blue-light)" };
  if (weather.trackTemp >= 45) return { label: "Hot & dry", color: "var(--orange)" };
  if (weather.trackTemp <= 20) return { label: "Cool & dry", color: "var(--blue-lighter)" };
  return { label: "Dry", color: "var(--green)" };
}

export const WeatherPanel = memo(function WeatherPanel({ weather }: { weather: Weather }) {
  const now = condition(weather);
  const trackFill = Math.min(100, Math.max(0, ((weather.trackTemp - TRACK_MIN) / (TRACK_MAX - TRACK_MIN)) * 100));
  const airFill = Math.min(100, Math.max(0, ((weather.airTemp - AIR_MIN) / (AIR_MAX - AIR_MIN)) * 100));

  return (
    <section className={s.panel}>
      <div className={s.header}>
        <div className={s.marker} />
        <h2 className={s.title}>Weather</h2>
        <div className={s.rule} />
        <div className={s.condition} style={{ color: now.color }}>{now.label}</div>
      </div>

      <div className={s.primary}>
        <div className={s.tempCard}>
          <div className={s.tempIcon} style={{ color: tempColor(weather.trackTemp, TRACK_MIN, TRACK_MAX) }}>
            <TrackTempIcon />
          </div>
          <div className={s.tempBody}>
            <div className={s.tempLabel}>Track</div>
            <div className={s.tempValue}>
              {weather.trackTemp.toFixed(1)}<span className={s.tempUnit}>°C</span>
            </div>
            <div className={s.tempBar}>
              <div className={s.tempFill}
                   style={{ width: `${trackFill}%`, background: tempColor(weather.trackTemp, TRACK_MIN, TRACK_MAX) }} />
            </div>
          </div>
        </div>

        <div className={s.tempCard}>
          <div className={s.tempIcon} style={{ color: tempColor(weather.airTemp, AIR_MIN, AIR_MAX) }}>
            <AirTempIcon />
          </div>
          <div className={s.tempBody}>
            <div className={s.tempLabel}>Air</div>
            <div className={s.tempValue}>
              {weather.airTemp.toFixed(1)}<span className={s.tempUnit}>°C</span>
            </div>
            <div className={s.tempBar}>
              <div className={s.tempFill}
                   style={{ width: `${airFill}%`, background: tempColor(weather.airTemp, AIR_MIN, AIR_MAX) }} />
            </div>
          </div>
        </div>
      </div>

      <div className={s.secondary}>
        <div className={s.stat}>
          <span style={{ color: "var(--blue-lighter)" }}><HumidityIcon /></span>
          <div className={s.statBody}>
            <div className={s.statLabel}>Humidity</div>
            <div className={s.statValue}>{Math.round(weather.humidity)}<span className={s.statUnit}>%</span></div>
          </div>
        </div>

        <div className={s.stat}>
          <span style={{ color: "var(--text-dim)" }}><WindIcon degrees={weather.windDirection} /></span>
          <div className={s.statBody}>
            <div className={s.statLabel}>Wind {compass(weather.windDirection)}</div>
            <div className={s.statValue}>
              {weather.windSpeed.toFixed(1)}<span className={s.statUnit}> km/h</span>
            </div>
          </div>
        </div>

        <div className={s.stat}>
          <span style={{ color: "var(--text-dim)" }}><PressureIcon /></span>
          <div className={s.statBody}>
            <div className={s.statLabel}>Pressure</div>
            <div className={s.statValue}>
              {Math.round(weather.pressure)}<span className={s.statUnit}> mb</span>
            </div>
          </div>
        </div>

        <div className={s.stat}>
          <span style={{ color: weather.rainfall ? "var(--blue-light)" : "var(--text-faint)" }}>
            <RainIcon raining={weather.rainfall} />
          </span>
          <div className={s.statBody}>
            <div className={s.statLabel}>Rainfall</div>
            <div className={s.statValue}>{weather.rainfall ? "YES" : "NO"}</div>
          </div>
        </div>
      </div>

      <div className={s.rainRow}>
        <span className={weather.rainfall ? s.rainWet : s.rainDry}>
          {weather.rainfall
            ? "Rain on track — intermediates or wets in play"
            : "Track dry — slick compounds"}
        </span>
      </div>
    </section>
  );
});
