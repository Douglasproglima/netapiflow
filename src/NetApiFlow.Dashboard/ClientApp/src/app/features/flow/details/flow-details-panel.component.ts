import { Component, computed, input, output } from '@angular/core';
import { FlowTraceDetail, SpanDetail } from '../../../core/models';
import { formatDurationMicros } from '../../../shared/duration-format';
import { httpMethodTagClass } from '../../../shared/http-method-style';
import { buildWaterfall, computeUnexplainedTime } from './waterfall';
import { findNPlusOneGroup } from '../graph/graph-builder';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { statusLabelKey } from '../../../shared/status-label';

const W3C_TRACE_ID_TAG = 'w3c.trace_id';

/**
 * No span selected: request summary + waterfall. A span selected: its full
 * detail, including ancestors, children and the delta from the normal median duration of that
 * operation.
 */
@Component({
  selector: 'app-flow-details-panel',
  templateUrl: './flow-details-panel.component.html',
  styleUrl: './flow-details-panel.component.scss',
  imports: [TranslatePipe],
})
export class FlowDetailsPanelComponent {
  readonly trace = input.required<FlowTraceDetail>();
  readonly selectedSpan = input<SpanDetail | null>(null);
  readonly slowThresholdMicros = input.required<number>();
  readonly notExecutedCount = input(0);

  readonly backToSummary = output<void>();
  readonly jumpToSpan = output<string>();

  readonly waterfall = computed(() => buildWaterfall(this.trace(), this.slowThresholdMicros()));

  readonly w3cTraceId = computed(() => this.trace().tags[W3C_TRACE_ID_TAG] ?? null);

  readonly unexplainedTimeValues = computed(() => {
    const unexplained = computeUnexplainedTime(this.trace());
    if (!unexplained) {
      return null;
    }

    const roundedPercent = unexplained.unexplainedPercent.toFixed(0);
    return {
      percent: roundedPercent === '0' && unexplained.unexplainedMicros > 0 ? '<1' : roundedPercent,
      unexplained: this.formatDuration(unexplained.unexplainedMicros),
      total: this.formatDuration(this.trace().durationMicros),
    };
  });

  readonly statusLabelKey = statusLabelKey;

  readonly executedCount = computed(() => this.trace().spans.length);

  /** siblings of the same shape as the selected span, anywhere in the trace. */
  readonly nPlusOneGroup = computed(() => {
    const selected = this.selectedSpan();
    return selected ? findNPlusOneGroup(this.trace().spans, selected.span.spanId) : [];
  });

  jumpToFirstNPlusOne(): void {
    const first = this.nPlusOneGroup()[0];
    if (first) {
      this.jumpToSpan.emit(first.spanId);
    }
  }

  methodTagClass(method: string): string {
    return httpMethodTagClass(method);
  }

  isWarningLevel(level: string): boolean {
    return level === 'Warning';
  }

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

  /** `HH:mm:ss.SSS` in the viewer's local time (guide seção 6.6); the ISO string stays in `title`. */
  formatLocalTime(iso: string | null): string {
    if (!iso) {
      return '—';
    }

    const date = new Date(iso);
    const pad = (n: number, width = 2) => String(n).padStart(width, '0');
    return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}.${pad(date.getMilliseconds(), 3)}`;
  }
}
