import { Component, computed, effect, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { EndpointDescriptor, RouteStat } from '../../core/models';
import { formatDurationMicros } from '../../shared/duration-format';
import { PeriodPreset, periodPresetFrom } from '../../shared/period-preset';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationKey } from '../../core/i18n/translations';

const WINDOWS: readonly PeriodPreset[] = ['24h', '7d', '30d'];
const DEFAULT_WINDOW: PeriodPreset = '7d';
const TOP_N = 10;

function routeKey(method: string, routeTemplate: string): string {
  return `${method.toUpperCase()} ${routeTemplate}`;
}

/**
 * Aggregated view across every traceable route, from GET /metrics/routes and GET /endpoints
 * (D-81): slowest routes and top errors are two sortings of the same payload, dead routes is the
 * frontend cross-reference between the two endpoints, not a field of either.
 */
@Component({
  selector: 'app-insights',
  templateUrl: './insights.component.html',
  styleUrl: './insights.component.scss',
  imports: [MatCardModule, TranslatePipe],
})
export class InsightsComponent {
  private readonly api = inject(ApiService);

  readonly windows = WINDOWS;
  readonly window = signal<PeriodPreset>(DEFAULT_WINDOW);

  readonly loading = signal(true);
  readonly error = signal<TranslationKey | null>(null);
  readonly routeStats = signal<RouteStat[]>([]);
  readonly endpoints = signal<EndpointDescriptor[]>([]);
  readonly tailSamplingEnabled = signal(false);
  readonly successSampleRate = signal(0);

  readonly tailSamplingRateLabel = computed(() => `${Math.round(this.successSampleRate() * 100)}%`);

  readonly slowestRoutes = computed(() =>
    [...this.routeStats()]
      .sort((a, b) => b.p95DurationMicros - a.p95DurationMicros)
      .slice(0, TOP_N),
  );

  readonly topErrors = computed(() =>
    [...this.routeStats()]
      .filter((stat) => stat.errorCount > 0)
      .sort((a, b) => b.errorCount - a.errorCount)
      .slice(0, TOP_N),
  );

  readonly deadRoutes = computed(() => {
    const executed = new Set(
      this.routeStats().map((stat) => routeKey(stat.method, stat.routeTemplate)),
    );
    return this.endpoints().filter(
      (endpoint) => !executed.has(routeKey(endpoint.method, endpoint.routeTemplate)),
    );
  });

  constructor() {
    this.api.getConfig().subscribe((config) => {
      this.tailSamplingEnabled.set(config.tailSamplingEnabled);
      this.successSampleRate.set(config.successSampleRate);
    });

    effect(() => {
      const from = periodPresetFrom(this.window(), new Date());
      this.loading.set(true);
      this.error.set(null);

      forkJoin([this.api.getRouteMetrics({ from }), this.api.getEndpoints()]).subscribe({
        next: ([routeStats, endpoints]) => {
          this.routeStats.set(routeStats);
          this.endpoints.set(endpoints);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('insights.error');
          this.loading.set(false);
        },
      });
    });
  }

  selectWindow(window: PeriodPreset): void {
    this.window.set(window);
  }

  windowLabelKey(window: PeriodPreset): TranslationKey {
    return `insights.window.${window}` as TranslationKey;
  }

  formatDuration(micros: number): string {
    return formatDurationMicros(micros);
  }

  trackByRoute(_index: number, stat: RouteStat): string {
    return routeKey(stat.method, stat.routeTemplate);
  }

  trackByEndpoint(_index: number, endpoint: EndpointDescriptor): string {
    return routeKey(endpoint.method, endpoint.routeTemplate);
  }
}
