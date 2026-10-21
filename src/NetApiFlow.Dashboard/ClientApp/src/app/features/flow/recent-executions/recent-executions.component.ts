import { Component, OnDestroy, inject, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { ApiService } from '../../../core/api.service';
import { SseService } from '../../../core/sse.service';
import { FlowTraceSummary } from '../../../core/models';
import { formatDurationMicros } from '../../../shared/duration-format';
import { httpMethodTagClass } from '../../../shared/http-method-style';
import { statusTint, StatusTint } from '../../../shared/status-tint';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslationKey } from '../../../core/i18n/translations';

const PAGE_SIZE = 8;

/**
 * Condensed recent-executions list for the `/flow` workspace (D-31, tarefa 7.0c): the first page
 * of `GET /traces` (the same route `/history` already uses in full), so the last few requests are
 * visible without leaving the main screen. Clicking a row opens it the same way the live-in-progress
 * list already does (`openTrace`, reused by the parent).
 */
@Component({
  selector: 'app-recent-executions',
  templateUrl: './recent-executions.component.html',
  styleUrl: './recent-executions.component.scss',
  imports: [MatCardModule, MatButtonModule, RouterLink, TranslatePipe],
})
export class RecentExecutionsComponent implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly language = inject(LanguageService);
  private readonly sse = inject(SseService);
  private readonly sseSubscription = this.sse.events$.subscribe((event) => {
    if (event.type === 'TraceCompleted') {
      this.loadTraces();
    }
  });

  readonly loading = signal(true);
  readonly error = signal<TranslationKey | null>(null);
  readonly traces = signal<FlowTraceSummary[]>([]);
  readonly collapsed = signal(false);

  readonly traceSelected = output<string>();

  toggleCollapsed(): void {
    this.collapsed.update((collapsed) => !collapsed);
  }

  constructor() {
    this.loadTraces();
  }

  ngOnDestroy(): void {
    this.sseSubscription.unsubscribe();
  }

  private loadTraces(): void {
    this.api.getTraces({ page: 1, pageSize: PAGE_SIZE }).subscribe({
      next: (result) => {
        this.traces.set(result.items);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('recent.error');
        this.loading.set(false);
      },
    });
  }

  methodTagClass(method: string): string {
    return httpMethodTagClass(method);
  }

  formatDuration(micros: number | null): string {
    return formatDurationMicros(micros);
  }

  statusTint(trace: FlowTraceSummary): StatusTint {
    return statusTint(trace.status);
  }

  /** `now` / `56 s` / `12 min`, relative to render time (guide seção 6.7); the ISO stays in `title`. */
  relativeTime(startedAt: string): string {
    const diffSeconds = Math.max(0, Math.floor((Date.now() - Date.parse(startedAt)) / 1000));
    if (diffSeconds < 5) {
      return this.language.translate('recent.relativeTime.now');
    }

    if (diffSeconds < 60) {
      return this.language.translate('recent.relativeTime.seconds', { n: diffSeconds });
    }

    return this.language.translate('recent.relativeTime.minutes', {
      n: Math.floor(diffSeconds / 60),
    });
  }
}
