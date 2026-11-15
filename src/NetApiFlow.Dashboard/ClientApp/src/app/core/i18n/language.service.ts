import { Injectable, computed, signal } from '@angular/core';
import { SupportedLanguage, TRANSLATIONS, TranslationKey } from './translations';

const STORAGE_KEY = 'naf-lang';
const DEFAULT_LANGUAGE: SupportedLanguage = 'pt-BR'; // Deliberately not navigator.language.

export function interpolate(template: string, values: Record<string, string | number>): string {
  return Object.keys(values).reduce(
    (text, placeholder) => text.replaceAll(`{${placeholder}}`, String(values[placeholder])),
    template,
  );
}

function readStoredLanguage(): SupportedLanguage {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored === 'pt-BR' || stored === 'en' ? stored : DEFAULT_LANGUAGE;
  } catch {
    return DEFAULT_LANGUAGE;
  }
}

/**
 * Runtime i18n: a dictionary per language inside the same embedded bundle, swapped by
 * a signal, no page reload and no per-language `dist/` output.
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  readonly language = signal<SupportedLanguage>(readStoredLanguage());

  readonly dictionary = computed(() => TRANSLATIONS[this.language()]);

  setLanguage(language: SupportedLanguage): void {
    this.language.set(language);
    try {
      localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // Storage may be unavailable (private browsing, exhausted quota, a blocked iframe); the
      // chosen language still applies for the rest of this session, it just won't be remembered.
    }
  }

  translate(key: TranslationKey, values?: Record<string, string | number>): string {
    const template = this.dictionary()[key];
    return values ? interpolate(template, values) : template;
  }
}
