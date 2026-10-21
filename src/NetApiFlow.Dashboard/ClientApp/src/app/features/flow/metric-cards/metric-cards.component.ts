import { Component, computed, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/api.service';
import { formatDurationMicros } from '../../../shared/duration-format';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationKey } from '../../../core/i18n/translations';

export type MetricCardIcon = 'traces' | 'success' | 'errors' | 'avg' | 'max' | 'steps';
export type MetricCardTint = 'flow' | 'success' | 'warning' | 'danger' | 'idle';

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
 * The metric strip at the top of the `/flow` workspace (ADR-0010, guide seção 6.2): total traces,
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
export class MetricCardsComponent {
  private readonly api = inject(ApiService);

  readonly loading = signal(true);
  readonly error = signal<TranslationKey | null>(null);

  private readonly totalTraces = signal(0);
  private readonly errorCount = signal(0);
  private readonly averageDurationMicros = signal(0);
  private readonly maxDurationMicros = signal(0);
  private readonly stepsExecuted = signal(0);
  private readonly slowThresholdMicros = signal(Infinity);

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

    this.api.getMetrics({}).subscribe({
      next: (summary) => {
        this.totalTraces.set(summary.totalTraces);
        this.errorCount.set(summary.errorCount);
        this.averageDurationMicros.set(summary.averageDurationMicros);
        this.maxDurationMicros.set(summary.maxDurationMicros);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('metrics.error');
        this.loading.set(false);
      },
    });

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
}
