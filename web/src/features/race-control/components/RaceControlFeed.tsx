import { useMemo, useState } from "react";
import type { MessageCategory, RaceControlMessage } from "@/features/live/model/types";
import s from "./RaceControlFeed.module.css";

const ACCENT: Record<MessageCategory, string> = {
  "Yellow flag": "var(--yellow)",
  "Green flag": "var(--green)",
  "Blue flag": "var(--blue-lighter)",
  "Penalty": "var(--red-bright)",
  "Lap deleted": "var(--text-dim)",
  "Investigation": "var(--yellow)",
  "Car event": "var(--red-bright)",
  "DRS": "var(--blue-light)",
  "VSC": "var(--yellow)",
  "Safety car": "var(--yellow)",
  "Other": "var(--text-dim)",
};

type Filter = "ALL" | "FLAGS" | "SC/VSC" | "DRS" | "CARS";

const MATCHES: Record<Filter, (m: RaceControlMessage) => boolean> = {
  "ALL": () => true,
  "FLAGS": (m) => m.category.endsWith("flag"),
  "SC/VSC": (m) => m.category === "Safety car" || m.category === "VSC",
  "DRS": (m) => m.category === "DRS",
  "CARS": (m) =>
    m.category === "Penalty" || m.category === "Car event" ||
    m.category === "Lap deleted" || m.category === "Investigation",
};

export function RaceControlFeed({ messages }: { messages: RaceControlMessage[] }) {
  const [filter, setFilter] = useState<Filter>("ALL");
  const visible = useMemo(() => messages.filter(MATCHES[filter]), [messages, filter]);

  return (
    <section className={s.panel}>
      <div className={s.header}>
        <div className={s.marker} />
        <h2 className={s.title}>Race control log</h2>
        <div className={s.count}>{messages.length} TODAY</div>
        <div className={s.spacer} />
        <div className={s.filters} role="group" aria-label="Filter messages">
          {(Object.keys(MATCHES) as Filter[]).map((f) => (
            <button
              key={f}
              type="button"
              className={`${s.filter} ${filter === f ? s.filterActive : ""}`}
              aria-pressed={filter === f}
              onClick={() => setFilter(f)}
            >
              {f}
            </button>
          ))}
        </div>
      </div>

      <div className={s.list}>
        {visible.length === 0 ? (
          <div className={s.empty}>NO MESSAGES</div>
        ) : (
          visible.map((m, i) => (
            <article key={`${m.time}-${i}`} className={`${s.entry} ${m.highlighted ? s.entryHot : ""}`}>
              <div className={s.accent} style={{ background: ACCENT[m.category] }} />
              <div className={s.body}>
                <div className={s.meta}>
                  <span className={s.category} style={{ color: ACCENT[m.category] }}>
                    {m.category}
                  </span>
                  <span className={s.lap}>LAP {m.lap}</span>
                  <div className={s.spacer} />
                  <span className={s.time}>{m.time}</span>
                </div>
                <p className={s.text}>{m.text}</p>
              </div>
            </article>
          ))
        )}
      </div>
    </section>
  );
}
