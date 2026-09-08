import s from "./SupportPage.module.css";

/**
 * Where the Patreon link will live.
 *
 * The link is not set yet, so the button says so instead of pointing nowhere.
 * When the URL arrives, replace PATREON_URL with it and the pending state
 * disappears on its own.
 */
const PATREON_URL: string | null = null;

export function SupportPage() {
  return (
    <div className={s.page}>
      <div className={s.hero}>
        <div className={s.kicker}>Support</div>
        <h1 className={s.title}>Keep Apex running</h1>
        <p className={s.lead}>
          Apex is <strong>free and self-hostable</strong>, built and maintained by one person
          at CloudSheep. There is no subscription, no account and no tracking — but there is a
          server, a domain and a lot of weekends.
        </p>
      </div>

      <div className={s.rule} />

      <div className={s.grid}>
        <div className={s.card}>
          <div className={s.cardTitle}>What it pays for</div>
          <p className={s.cardBody}>
            The server that ingests the live feed and stores the session archive — every race,
            qualifying and practice session back to 2018, kept so they can be replayed without
            downloading them again.
          </p>
        </div>

        <div className={s.card}>
          <div className={s.cardTitle}>What it does not change</div>
          <p className={s.cardBody}>
            Nothing goes behind a paywall. No feature is held back for supporters, and no data
            is collected from anyone. Supporting is a thank-you, not a licence.
          </p>
        </div>

        <div className={s.card}>
          <div className={s.cardTitle}>Other ways to help</div>
          <p className={s.cardBody}>
            Run your own instance, report what breaks during a race weekend, or tell someone who
            watches F1 with a second screen. All of that is worth as much as money.
          </p>
        </div>
      </div>

      <div className={s.actions}>
        {PATREON_URL ? (
          <a className={s.primary} href={PATREON_URL} target="_blank" rel="noreferrer noopener">
            Support on Patreon
          </a>
        ) : (
          <span className={s.pending} aria-disabled="true">
            Patreon — coming soon
          </span>
        )}

        <a className={s.secondary} href="https://cloudsheep.dev" target="_blank" rel="noreferrer noopener">
          About CloudSheep
        </a>
      </div>

      <p className={s.note}>
        Apex is unofficial and unaffiliated with the Formula 1 companies. Supporting the project
        supports the person who builds it, and nothing else.
      </p>
    </div>
  );
}
