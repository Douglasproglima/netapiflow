import { Injectable, computed, signal } from '@angular/core';
import { SupportedLanguage, TRANSLATIONS, TranslationKey } from './translations';

const STORAGE_KEY = 'naf-lang';
const DEFAULT_LANGUAGE: SupportedLanguage = 'pt-BR'; // D-35: never navigator.language

function readStoredLanguage(): SupportedLanguage {
  const stored = localStorage.getItem(STORAGE_KEY);
  return stored === 'pt-BR' || stored === 'en' ? stored : DEFAULT_LANGUAGE;
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
    localStorage.setItem(STORAGE_KEY, language);
  }

  translate(key: TranslationKey, values?: Record<string, string | number>): string {
    const template = this.dictionary()[key];
    if (!values) {
      return template;
    }

    return Object.keys(values).reduce(
      (text, placeholder) => text.replace(`{${placeholder}}`, String(values[placeholder])),
      template,
    );
  }
}
