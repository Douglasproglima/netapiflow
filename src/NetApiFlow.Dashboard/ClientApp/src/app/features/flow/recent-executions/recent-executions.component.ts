import { Component, OnDestroy, computed, inject, model, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { auditTime, filter, map, merge, of, switchMap } from 'rxjs';
import { ApiService } from '../../../core/api.service';
import { SSE_REFRESH_AUDIT_MS, SseService } from '../../../core/sse.service';
import { FlowTraceSummary } from '../../../core/models';
import { formatDurationMicros } from '../../../shared/duration-format';
import { httpMethodTagClass } from '../../../shared/http-method-style';
import { catchAsResult } from '../../../shared/observable-result';
import { statusTint, StatusTint } from '../../../shared/status-tint';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslationKey } from '../../../core/i18n/translations';

const PAGE_SIZE = 8;
const MOBILE_CARD_BREAKPOINT = '(max-width: 767px)';
const MOBILE_VISIBLE_COUNT = 4;

/**
 * Condensed recent-executions list for the `/flow` workspace: the first page
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
  private readonly breakpointObserver = inject(BreakpointObserver);

  readonly loading = signal(true);
  readonly error = signal<TranslationKey | null>(null);
  readonly traces = signal<FlowTraceSummary[]>([]);

  private readonly isMobileCard = toSignal(
    this.breakpointObserver.observe(MOBILE_CARD_BREAKPOINT).pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  /**
   * Below 767px this card no longer gets its own scroll: a finger landing on a fixed-height list
   * nested inside a page that already scrolls just gets stuck scrolling the inner list instead.
   * A short, fixed slice plus "Ver todas" replaces that scroll instead of hiding it behind one.
   */
  readonly visibleTraces = computed(() => {
    const traces = this.traces();
    return this.isMobileCard() ? traces.slice(0, MOBILE_VISIBLE_COUNT) : traces;
  });
  /**
   * `model()`, not a plain internal signal: the parent workspace needs to know when this card
   * collapses so it can shrink the shared footer row and return the freed height to the graph,
   * which a signal private to this component could never report upward.
   */
  readonly collapsed = model(false);

  readonly traceSelected = output<string>();

  /** Refreshes at most once per `SSE_REFRESH_AUDIT_MS` window (A2), cancelling a stale in-flight request. */
  private readonly subscription = merge(
    of(undefined),
    this.sse.events$.pipe(
      filter((event) => event.type === 'TraceCompleted'),
      auditTime(SSE_REFRESH_AUDIT_MS),
    ),
  )
    .pipe(switchMap(() => catchAsResult(this.api.getTraces({ page: 1, pageSize: PAGE_SIZE }))))
    .subscribe((result) => {
      if (!result.ok) {
        this.error.set('recent.error');
        this.loading.set(false);
        return;
      }

      this.error.set(null);
      this.traces.set(result.value.items);
      this.loading.set(false);
    });

  toggleCollapsed(): void {
    this.collapsed.update((collapsed) => !collapsed);
  }

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
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

  /** `now` / `56 s` / `12 min` / `3 h` / `2 d`, relative to render time; the ISO stays in `title`. */
  relativeTime(startedAt: string): string {
    const diffSeconds = Math.max(0, Math.floor((Date.now() - Date.parse(startedAt)) / 1000));
    if (diffSeconds < 5) {
      return this.language.translate('recent.relativeTime.now');
    }

    if (diffSeconds < 60) {
      return this.language.translate('recent.relativeTime.seconds', { n: diffSeconds });
    }

    const diffMinutes = Math.floor(diffSeconds / 60);
    if (diffMinutes < 60) {
      return this.language.translate('recent.relativeTime.minutes', { n: diffMinutes });
    }

    const diffHours = Math.floor(diffMinutes / 60);
    if (diffHours < 24) {
      return this.language.translate('recent.relativeTime.hours', { n: diffHours });
    }

    return this.language.translate('recent.relativeTime.days', { n: Math.floor(diffHours / 24) });
  }
}
