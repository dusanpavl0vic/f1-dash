import type { Toast } from "../lib/useToasts";
import s from "./ToastStack.module.css";

/**
 * Session-state notifications.
 *
 * `role="status"` and `aria-live="polite"` rather than `alert`: these announce
 * themselves after whatever the user is currently reading, which is right even
 * for a red flag — the banner in the header is the urgent channel, this is the
 * record of what changed.
 */
export function ToastStack({ toasts, onDismiss }: { toasts: Toast[]; onDismiss(id: number): void }) {
  if (toasts.length === 0) return null;

  return (
    <div className={s.stack} role="status" aria-live="polite">
      {toasts.map((toast) => (
        <div key={toast.id} className={`${s.toast} ${s[toast.tone]}`}>
          <div className={s.body}>
            <div className={s.title}>{toast.title}</div>
            {toast.detail && <div className={s.detail}>{toast.detail}</div>}
          </div>
          <button
            type="button"
            className={s.close}
            aria-label={`Dismiss ${toast.title}`}
            onClick={() => onDismiss(toast.id)}
          >
            ×
          </button>
        </div>
      ))}
    </div>
  );
}
