import { SETTINGS, useSetting } from "@/hooks/useSetting";
import s from "./SettingsPage.module.css";

export function SettingsPage() {
  const [photos, setPhotos] = useSetting(SETTINGS.driverPhotos, true);

  return (
    <div className={s.page}>
      <h1 className={s.title}>Settings</h1>
      <p className={s.lead}>
        Stored in this browser only. Nothing is sent anywhere, and there is no account to
        sign in to.
      </p>

      <div className={s.group}>
        <div className={s.groupTitle}>Appearance</div>

        <div className={s.row}>
          <div className={s.rowText}>
            <div className={s.rowTitle}>Driver photographs</div>
            <p className={s.rowBody}>
              Headshots arrive in the timing feed itself and are loaded from{" "}
              <strong>F1&rsquo;s media server</strong>. For a dashboard you run yourself this is a
              grey area rather than a clear right, so it is a switch instead of a decision made
              for you — and every page is designed to be complete without them, falling back to
              the driver&rsquo;s initials and racing number.
              <br /><br />
              If you deploy this publicly rather than for yourself, consider turning it off.
            </p>
          </div>

          <span className={s.switch}>
            <input
              className={s.input}
              type="checkbox"
              aria-label="Show driver photographs"
              checked={photos}
              onChange={(e) => setPhotos(e.target.checked)}
            />
            <span className={s.track} aria-hidden="true"><span className={s.thumb} /></span>
          </span>
        </div>
      </div>
    </div>
  );
}
