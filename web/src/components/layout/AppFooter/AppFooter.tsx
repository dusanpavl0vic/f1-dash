import s from "./AppFooter.module.css";

/**
 * The legal notice is verbatim and mandatory on every page (docs/01). No F1
 * logo, wordmark or branding appears anywhere in the application.
 */
export function AppFooter() {
  return (
    <footer className={s.footer}>
      <div className={s.legalBlock}>
      <p className={s.legal}>
        This project is unofficial and is not associated in any way with the Formula 1 companies.
        F1, FORMULA ONE, FORMULA 1, FIA FORMULA ONE WORLD CHAMPIONSHIP, GRAND PRIX and related
        marks are trade marks of Formula One Licensing B.V.
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
      <div className={s.links}>
        <span>Schedule</span>
        <span>Replay</span>
        <span>Standings</span>
        <span>Settings</span>
      </div>
    </footer>
  );
}
