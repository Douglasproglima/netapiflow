import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { ApiService } from '../../../core/api.service';
import { FlowTraceDetail, NodeVisualState } from '../../../core/models';
import { httpMethodTagClass } from '../../../shared/http-method-style';
import { formatDurationMicros } from '../../../shared/duration-format';
import { NODE_STATE_PRESENTATION } from '../../../shared/node-state-presentation';
import { formatStartedAtShort } from '../../../shared/started-at-format';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationKey } from '../../../core/i18n/translations';
import { buildTraceDiff, DiffRow } from './trace-diff-builder';

type ViewState = 'missing-params' | 'loading' | 'error' | 'data';

function describeError(error: HttpErrorResponse): string {
  return error.status ? `HTTP ${error.status} ${error.statusText}`.trim() : error.message;
}

/** `/diff?a=<traceId>&b=<traceId>`: side-by-side comparison of two executions. */
@Component({
  selector: 'app-diff-page',
  templateUrl: './diff-page.component.html',
  styleUrl: './diff-page.component.scss',
  imports: [RouterLink, TranslatePipe],
})
export class DiffPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(ApiService);

  readonly traceIdA = toSignal(this.route.queryParamMap.pipe(map((params) => params.get('a'))), {
    initialValue: this.route.snapshot.queryParamMap.get('a'),
  });
  readonly traceIdB = toSignal(this.route.queryParamMap.pipe(map((params) => params.get('b'))), {
    initialValue: this.route.snapshot.queryParamMap.get('b'),
  });

  readonly traceA = signal<FlowTraceDetail | null>(null);
  readonly traceB = signal<FlowTraceDetail | null>(null);
  readonly slowThresholdMicros = signal<number | null>(null);

  private readonly loadingA = signal(false);
  private readonly loadingB = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly viewState = computed<ViewState>(() => {
    if (!this.traceIdA() || !this.traceIdB()) {
      return 'missing-params';
    }
    if (this.errorMessage()) {
      return 'error';
    }
    if (this.loadingA() || this.loadingB() || !this.traceA() || !this.traceB()) {
      return 'loading';
    }
    return 'data';
  });

  readonly diffRows = computed<DiffRow[]>(() => {
    const traceA = this.traceA();
    const traceB = this.traceB();
    const slowThresholdMicros = this.slowThresholdMicros();
    if (!traceA || !traceB || slowThresholdMicros === null) {
      return [];
    }

    return buildTraceDiff({ traceA, traceB, slowThresholdMicros });
  });

  readonly onlyInACount = computed(
    () => this.diffRows().filter((row) => row.kind === 'onlyInA').length,
  );
  readonly onlyInBCount = computed(
    () => this.diffRows().filter((row) => row.kind === 'onlyInB').length,
  );

  readonly routeMismatch = computed(() => {
    const traceA = this.traceA();
    const traceB = this.traceB();
    if (!traceA || !traceB) {
      return false;
    }

    return traceA.method !== traceB.method || traceA.routeTemplate !== traceB.routeTemplate;
  });

  constructor() {
    this.api
      .getConfig()
      .subscribe((config) => this.slowThresholdMicros.set(config.slowThresholdMicros));

    effect(() => {
      const idA = this.traceIdA();
      const idB = this.traceIdB();
      if (!idA || !idB) {
        return;
      }

      this.errorMessage.set(null);

      this.loadingA.set(true);
      this.api.getTrace(idA).subscribe({
        next: (trace) => {
          this.traceA.set(trace);
          this.loadingA.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.errorMessage.set(describeError(error));
          this.loadingA.set(false);
        },
      });

      this.loadingB.set(true);
      this.api.getTrace(idB).subscribe({
        next: (trace) => {
          this.traceB.set(trace);
          this.loadingB.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.errorMessage.set(describeError(error));
          this.loadingB.set(false);
        },
      });
    });
  }

  methodTagClass(method: string): string {
    return httpMethodTagClass(method);
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

  stateIconSrc(state: NodeVisualState): string {
    return NODE_STATE_PRESENTATION[state].iconSrc;
  }

  stateLabelKey(state: NodeVisualState): TranslationKey {
    return NODE_STATE_PRESENTATION[state].labelKey;
  }

  traceState(trace: FlowTraceDetail): NodeVisualState {
    if (trace.status === 'Running') {
      return 'running';
    }

    if (trace.status === 'Cancelled') {
      return 'cancelled';
    }

    if (trace.status === 'Timeout') {
      return 'timeout';
    }

    return trace.status === 'Error' ? 'error' : 'completed';
  }

  startedAtShort(startedAt: string): string {
    return formatStartedAtShort(new Date(startedAt));
  }
}
