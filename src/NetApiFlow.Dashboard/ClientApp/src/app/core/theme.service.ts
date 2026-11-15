import { Injectable, signal } from '@angular/core';

export type Theme = 'dark' | 'light';

const STORAGE_KEY = 'naf-theme';

function readStoredTheme(): Theme | null {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored === 'dark' || stored === 'light' ? stored : null;
  } catch {
    return null;
  }
}

function prefersLight(): boolean {
  return matchMedia('(prefers-color-scheme: light)').matches;
}

function resolveIsDark(preference: Theme | null): boolean {
  return preference ? preference === 'dark' : !prefersLight();
}

function applyDomAttribute(preference: Theme | null): void {
  if (preference) {
    document.documentElement.dataset['theme'] = preference;
  } else {
    delete document.documentElement.dataset['theme'];
  }
}

/**
 * Manual theme toggle: `null` means no saved preference, so `prefers-color-scheme` stays
 * in charge, same as before this existed. Picking a theme writes `data-theme` on the document
 * root and persists it, overriding the media query from then on.
 *
 * Both the signal and the DOM attribute are updated synchronously wherever `preference` changes
 * (just `toggle()` below), deliberately not through an `effect()`: an effect's callback runs on
 * the next change-detection flush, not synchronously on the write that triggered it, which would
 * leave `isDark()`/`data-theme` briefly stale right after `toggle()` returns.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private preference = readStoredTheme();

  readonly isDark = signal(resolveIsDark(this.preference));

  constructor() {
    applyDomAttribute(this.preference);
  }

  toggle(): void {
    this.preference = this.isDark() ? 'light' : 'dark';
    try {
      localStorage.setItem(STORAGE_KEY, this.preference);
    } catch {
      // Storage may be unavailable (private browsing, exhausted quota, a blocked iframe); the
      // toggle still works for the rest of this session, it just won't be remembered next time.
    }
    this.isDark.set(resolveIsDark(this.preference));
    applyDomAttribute(this.preference);
  }
}
