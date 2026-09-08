import { Link } from "react-router";
import s from "./AppFooter.module.css";

/**
 * The site footer.
 *
 * The legal notice is verbatim and mandatory on every page (docs/01), and no F1
 * logo, wordmark or branding appears anywhere in the application. It sits in a
 * separate band below the navigation because it is a disclaimer, not a link —
 * mixing the two makes both harder to read.
 */
export function AppFooter() {
  return (
    <footer className={s.footer}>
      <div className={s.columns}>
        <div className={s.brandCol}>
          <div className={s.brandName}>Ap<em>e</em>x</div>
          <p className={s.brandTag}>
            Self-hosted Formula 1 <strong>live timing</strong>, <strong>replay</strong> and{" "}
            <strong>telemetry</strong>. Every session back to 2018, replayed from the archive
            or followed as it happens.
          </p>
        </div>

        <div className={s.col}>
          <div className={s.colTitle}>Watch</div>
          <Link to="/live">Live timing</Link>
          <Link to="/replay">Replay a session</Link>
          <Link to="/schedule">Schedule</Link>
        </div>

        <div className={s.col}>
          <div className={s.colTitle}>Explore</div>
          <Link to="/results">Results</Link>
          <Link to="/standings">Standings</Link>
          <Link to="/telemetry">Telemetry</Link>
          <Link to="/drivers">Drivers</Link>
          <Link to="/teams">Teams</Link>
        </div>

        <div className={s.col}>
          <div className={s.colTitle}>Project</div>
          <a href="https://cloudsheep.dev" target="_blank" rel="noreferrer noopener">
            CloudSheep
          </a>
          <Link to="/settings">Settings</Link>
          <Link to="/support" className={s.support}>Support Apex</Link>
        </div>
      </div>

      <div className={s.bottom}>
        <div className={s.bottomInner}>
          <div>
            <p className={s.legal}>
              This project is unofficial and is not associated in any way with the Formula 1
              companies. F1, FORMULA ONE, FORMULA 1, FIA FORMULA ONE WORLD CHAMPIONSHIP, GRAND
              PRIX and related marks are trade marks of Formula One Licensing B.V.
            </p>
            <p className={s.credit}>
              Loading indicator:{" "}
              <a href="https://commons.wikimedia.org/wiki/File:F1_tire_Pirelli_PZero_Red.svg"
                 target="_blank" rel="noreferrer noopener">F1 tyre</a>{" "}
              by MiniPictures,{" "}
              <a href="https://creativecommons.org/licenses/by-sa/4.0/"
                 target="_blank" rel="noreferrer noopener">CC BY-SA 4.0</a>.
            </p>
          </div>

          <a
            className={s.owner}
            href="https://cloudsheep.dev"
            target="_blank"
            rel="noreferrer noopener"
          >
            <span className={s.ownerLabel}>Built by</span>
            <span className={s.ownerName}>CloudSheep</span>
          </a>
        </div>
      </div>
    </footer>
  );
}
