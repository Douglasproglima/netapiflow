import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/api.service';
import { SseService } from '../../../core/sse.service';
import { formatDurationMicros } from '../../../shared/duration-format';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationKey } from '../../../core/i18n/translations';

export type MetricCardIcon = 'traces' | 'success' | 'errors' | 'avg' | 'max' | 'steps';
export type MetricCardTint = 'flow' | 'success' | 'warning' | 'danger' | 'idle';

const SPARKLINE_SAMPLE_SIZE = 50;
const SPARKLINE_VIEWBOX_WIDTH = 120;
const SPARKLINE_VIEWBOX_HEIGHT = 32;
const SPARKLINE_VERTICAL_PADDING = 3;

interface MetricCard {
  readonly labelKey: TranslationKey;
  readonly value: string;
  readonly valueUnit: string | null;
  readonly icon: MetricCardIcon;
  readonly tint: MetricCardTint;
  /** True when the value itself is noteworthy (e.g. errors > 0) and should stand out in color. */
  readonly highlight: boolean;
}

/** Splits a formatted duration like `"98.1 ms"` into its number and unit, so the unit can render smaller/muted. */
function splitDuration(formatted: string): { value: string; unit: string | null } {
  const spaceIndex = formatted.indexOf(' ');
  return spaceIndex < 0
    ? { value: formatted, unit: null }
    : { value: formatted.slice(0, spaceIndex), unit: formatted.slice(spaceIndex + 1) };
}

/**
 * The metric strip at the top of the `/flow` workspace (guide seção 6.2): total traces,
 * success, errors, average/max latency and steps executed. `GET /metrics` already has every count
 * but steps executed, which is the sum of `executionCount` across `GET /metrics/components`
 * (D-30) — a legitimate derivation from data already exposed, not a new contract. The latency
 * tiles switch to the warning tint when the value is at or above `GET /config`'s slow threshold.
 */
@Component({
  selector: 'app-metric-cards',
  templateUrl: './metric-cards.component.html',
  styleUrl: './metric-cards.component.scss',
  imports: [TranslatePipe],
})
export class MetricCardsComponent implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly sse = inject(SseService);
  private readonly sseSubscription = this.sse.events$.subscribe((event) => {
    if (event.type === 'TraceCompleted') {
      this.loadMetrics();
      this.loadComponentMetrics();
      this.loadSparkline();
    }
  });

  readonly loading = signal(true);
  readonly error = signal<TranslationKey | null>(null);
  readonly sparklineSampleSize = SPARKLINE_SAMPLE_SIZE;

  private readonly totalTraces = signal(0);
  private readonly errorCount = signal(0);
  private readonly averageDurationMicros = signal(0);
  private readonly maxDurationMicros = signal(0);
  private readonly p95DurationMicros = signal(0);
  private readonly stepsExecuted = signal(0);
  private readonly slowThresholdMicros = signal(Infinity);
  private readonly sparklineDurations = signal<number[]>([]);

  readonly p95Value = computed(() => splitDuration(formatDurationMicros(this.p95DurationMicros())));

  readonly sparklinePoints = computed(() => {
    const durations = this.sparklineDurations();
    if (durations.length === 0) {
      return '';
    }

    const max = Math.max(...durations);
    const min = Math.min(...durations);
    const range = max - min || 1;
    const stepX = durations.length > 1 ? SPARKLINE_VIEWBOX_WIDTH / (durations.length - 1) : 0;
    const drawableHeight = SPARKLINE_VIEWBOX_HEIGHT - SPARKLINE_VERTICAL_PADDING * 2;

    return durations
      .map((value, index) => {
        const x = index * stepX;
        const y = SPARKLINE_VERTICAL_PADDING + (1 - (value - min) / range) * drawableHeight;
        return `${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(' ');
  });

  readonly cards = computed<MetricCard[]>(() => {
    const threshold = this.slowThresholdMicros();
    const avg = splitDuration(formatDurationMicros(this.averageDurationMicros()));
    const max = splitDuration(formatDurationMicros(this.maxDurationMicros()));

    return [
      {
        labelKey: 'metrics.traces',
        value: String(this.totalTraces()),
        valueUnit: null,
        icon: 'traces',
        tint: 'flow',
        highlight: false,
      },
      {
        labelKey: 'metrics.successful',
        value: String(this.totalTraces() - this.errorCount()),
        valueUnit: null,
        icon: 'success',
        tint: 'success',
        highlight: false,
      },
      {
        labelKey: 'metrics.errors',
        value: String(this.errorCount()),
        valueUnit: null,
        icon: 'errors',
        tint: this.errorCount() > 0 ? 'danger' : 'idle',
        highlight: this.errorCount() > 0,
      },
      {
        labelKey: 'metrics.avgLatency',
        value: avg.value,
        valueUnit: avg.unit,
        icon: 'avg',
        tint: this.averageDurationMicros() >= threshold ? 'warning' : 'flow',
        highlight: false,
      },
      {
        labelKey: 'metrics.maxLatency',
        value: max.value,
        valueUnit: max.unit,
        icon: 'max',
        tint: this.maxDurationMicros() >= threshold ? 'warning' : 'flow',
        highlight: false,
      },
      {
        labelKey: 'metrics.stepsExecuted',
        value: String(this.stepsExecuted()),
        valueUnit: null,
        icon: 'steps',
        tint: 'flow',
        highlight: false,
      },
    ];
  });

  constructor() {
    this.api
      .getConfig()
      .subscribe((config) => this.slowThresholdMicros.set(config.slowThresholdMicros));

    this.loadMetrics();
    this.loadComponentMetrics();
    this.loadSparkline();
  }

  ngOnDestroy(): void {
    this.sseSubscription.unsubscribe();
  }

  private loadMetrics(): void {
    this.api.getMetrics({}).subscribe({
      next: (summary) => {
        this.totalTraces.set(summary.totalTraces);
        this.errorCount.set(summary.errorCount);
        this.averageDurationMicros.set(summary.averageDurationMicros);
        this.maxDurationMicros.set(summary.maxDurationMicros);
        this.p95DurationMicros.set(summary.p95DurationMicros);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('metrics.error');
        this.loading.set(false);
      },
    });
  }

  private loadComponentMetrics(): void {
    this.api.getComponentMetrics({}).subscribe({
      next: (stats) => {
        const total = stats.reduce((sum, stat) => sum + stat.executionCount, 0);
        this.stepsExecuted.set(total);
      },
      error: () => {
        // Steps executed is a bonus figure on top of GET /metrics; its own failure doesn't block
        // the rest of the strip, which already has its own error state.
      },
    });
  }

  private loadSparkline(): void {
    this.api.getTraces({ page: 1, pageSize: SPARKLINE_SAMPLE_SIZE }).subscribe({
      next: (result) => {
        this.sparklineDurations.set(
          result.items
            .map((trace) => trace.durationMicros)
            .filter((value): value is number => value !== null)
            .reverse(),
        );
      },
      error: () => {
        // The sparkline is a supplementary visual on top of the p95 figure; its own failure
        // doesn't block the rest of the strip, which already has its own error state.
      },
    });
  }
}
