import { Component, computed, input } from '@angular/core';
import { FlowTraceDetail, SpanDetail } from '../../../core/models';
import { formatDurationMicros } from '../../../shared/duration-format';
import { buildWaterfall } from './waterfall';

/**
 * No span selected: request summary + waterfall. A span selected: its full detail, including
 * ancestors, children and the delta from the normal median duration of that operation (seção 8
 * do prompt).
 */
@Component({
  selector: 'app-flow-details-panel',
  templateUrl: './flow-details-panel.component.html',
  styleUrl: './flow-details-panel.component.scss',
})
export class FlowDetailsPanelComponent {
  readonly trace = input.required<FlowTraceDetail>();
  readonly selectedSpan = input<SpanDetail | null>(null);

  readonly waterfall = computed(() => buildWaterfall(this.trace()));

  formatDuration(micros: number | null): string {
    return formatDurationMicros(micros);
  }

  formatDelta(micros: number | null): string {
    if (micros === null) {
      return '—';
    }

    const sign = micros >= 0 ? '+' : '−';
    return `${sign}${formatDurationMicros(Math.abs(micros))}`;
  }
}
