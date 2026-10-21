import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from './language.service';
import { TranslationKey } from './translations';

/**
 * `{{ 'nav.flow' | t }}` / `{{ 'topbar.inProgress' | t: { count: inProgressCount() } }}`.
 * Impure: `LanguageService.language` changing triggers a global change-detection tick (something
 * in the shell template reads it directly), which re-evaluates every pipe instance, including
 * this one, even where nothing else in that component's own template reads a signal directly.
 */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly language = inject(LanguageService);

  transform(key: TranslationKey | undefined, values?: Record<string, string | number>): string {
    return key ? this.language.translate(key, values) : '';
  }
}
