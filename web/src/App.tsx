import { buildMockSession } from "./features/live/fixtures/mockSession";

/**
 * Placeholder shell. Replaced by the real application shell in IMPL-06.
 * It renders enough to prove the scaffold, tokens and fixtures all load.
 */
export function App() {
  const session = buildMockSession();
  return (
    <div style={{ width: "var(--app-width)", margin: "0 auto", padding: 40 }}>
      <div
        style={{
          fontFamily: "var(--font-display)",
          fontSize: "var(--fs-12)",
          fontWeight: 700,
          letterSpacing: "var(--ls-brand)",
          textTransform: "uppercase",
          color: "var(--accent)",
        }}
      >
        Apex
      </div>
      <div style={{ fontSize: "var(--fs-16)", marginTop: 8 }}>
        {session.session.meetingName} · lap {session.session.currentLap}/{session.session.totalLaps}
      </div>
      <div className="num" style={{ color: "var(--text-faint)", marginTop: 4, fontSize: "var(--fs-11)" }}>
        {session.timing.length} drivers · {session.messages.length} messages · scaffold OK
      </div>
    </div>
  );
}
