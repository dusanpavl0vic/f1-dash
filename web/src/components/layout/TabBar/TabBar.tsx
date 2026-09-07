import type { AppView, Weather } from "@/features/live/model/types";
import s from "./TabBar.module.css";

const TABS: { view: AppView; label: string }[] = [
  { view: "timing", label: "Race dashboard" },
  { view: "telemetry", label: "Telemetry wall" },
  { view: "notifications", label: "Race control" },
];

interface TabBarProps {
  view: AppView;
  messageCount: number;
  weather: Weather;
  onChange(view: AppView): void;
}

export function TabBar({ view, messageCount, weather, onChange }: TabBarProps) {
  return (
    <nav className={s.bar}>
      {TABS.map((tab) => (
        <button
          key={tab.view}
          type="button"
          className={`${s.tab} ${view === tab.view ? s.tabActive : ""}`}
          aria-current={view === tab.view ? "page" : undefined}
          onClick={() => onChange(tab.view)}
        >
          {tab.label}
          {tab.view === "notifications" && messageCount > 0 && (
            <span className={s.badge}>{messageCount}</span>
          )}
        </button>
      ))}

      <div className={s.spacer} />

      <div className={s.weather}>
        <div>WIND {weather.windSpeed.toFixed(1)} KM/H</div>
        <div>HUM {Math.round(weather.humidity)}%</div>
        <div>PRESS {Math.round(weather.pressure)}MB</div>
        <div>
          RAIN{" "}
          <span className={weather.rainfall ? s.rainYes : s.rainNo}>
            {weather.rainfall ? "YES" : "NO"}
          </span>
        </div>
      </div>
    </nav>
  );
}
