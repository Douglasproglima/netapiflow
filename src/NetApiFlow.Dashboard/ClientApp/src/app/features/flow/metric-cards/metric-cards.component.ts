import { Component, computed, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { ApiService } from '../../../core/api.service';
import { formatDurationMicros } from '../../../shared/duration-format';

interface MetricCard {
  readonly label: string;
  readonly value: string;
}

/**
 * The metric strip at the top of the `/flow` workspace (ADR-0010): total traces, success, errors,
 * average/max latency and steps executed. `GET /metrics` already has every count but steps
 * executed, which is the sum of `executionCount` across `GET /metrics/components` (D-30) — a
 * legitimate derivation from data already exposed, not a new contract.
 */
@Component({
  selector: 'app-metric-cards',
  templateUrl: './metric-cards.component.html',
  styleUrl: './metric-cards.component.scss',
  imports: [MatCardModule],
})
export class MetricCardsComponent {
  private readonly api = inject(ApiService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  private readonly totalTraces = signal(0);
  private readonly errorCount = signal(0);
  private readonly averageDurationMicros = signal(0);
  private readonly maxDurationMicros = signal(0);
  private readonly stepsExecuted = signal(0);

  readonly cards = computed<MetricCard[]>(() => [
    { label: 'Total traces', value: String(this.totalTraces()) },
    { label: 'Success', value: String(this.totalTraces() - this.errorCount()) },
    { label: 'Errors', value: String(this.errorCount()) },
    { label: 'Avg. latency', value: formatDurationMicros(this.averageDurationMicros()) },
    { label: 'Max. latency', value: formatDurationMicros(this.maxDurationMicros()) },
    { label: 'Steps executed', value: String(this.stepsExecuted()) },
  ]);

  constructor() {
    this.api.getMetrics({}).subscribe({
      next: (summary) => {
        this.totalTraces.set(summary.totalTraces);
        this.errorCount.set(summary.errorCount);
        this.averageDurationMicros.set(summary.averageDurationMicros);
        this.maxDurationMicros.set(summary.maxDurationMicros);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load metrics.');
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
