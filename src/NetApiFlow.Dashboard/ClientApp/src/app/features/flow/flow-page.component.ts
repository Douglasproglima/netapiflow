import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { BreakpointObserver } from '@angular/cdk/layout';
import { map } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { ActionDrawerService } from '../../core/action-drawer.service';
import { ApiService } from '../../core/api.service';
import { FlowTraceSummary, SpanDetail } from '../../core/models';
import { LiveTracePlayerService } from './playback/live-trace-player.service';
import { ReplayPlayerService } from './playback/replay-player.service';
import { buildGraph, GraphResult, isGhostNodeId } from './graph/graph-builder';
import { FlowGraphComponent } from './graph/flow-graph.component';
import { FlowDetailsPanelComponent } from './details/flow-details-panel.component';
import { MetricCardsComponent } from './metric-cards/metric-cards.component';
import { EventFeedComponent } from './event-feed/event-feed.component';
import { RecentExecutionsComponent } from './recent-executions/recent-executions.component';
import { EndpointFormComponent } from '../runner/endpoint-form.component';
import { httpMethodTagClass } from '../../shared/http-method-style';
import { formatDurationMicros } from '../../shared/duration-format';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LanguageService } from '../../core/i18n/language.service';

export const REPLAY_SPEEDS: readonly number[] = [0.25, 0.5, 1, 2, 4];

/**
 * Default screen (`/flow`, D-23): a dense workspace, metric cards always visible on
 * top, the action panel (shared `EndpointFormComponent`) on the left, the graph with replay
 * controls at the center, the details panel on the right, and the live event feed at the bottom.
 * With no `?trace=` query param the center column lists the traces currently in progress; with
 * one, it tracks that trace live (`LiveTracePlayerService`) or replays it
 * (`ReplayPlayerService`), rendering its graph and details panel either way.
 */
@Component({
  selector: 'app-flow-page',
  templateUrl: './flow-page.component.html',
  styleUrl: './flow-page.component.scss',
  imports: [
    FlowGraphComponent,
    FlowDetailsPanelComponent,
    MetricCardsComponent,
    EventFeedComponent,
    RecentExecutionsComponent,
    EndpointFormComponent,
    MatButtonModule,
    TranslatePipe,
  ],
  providers: [LiveTracePlayerService, ReplayPlayerService],
})
export class FlowPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  private readonly breakpointObserver = inject(BreakpointObserver);
  private readonly language = inject(LanguageService);
  readonly livePlayer = inject(LiveTracePlayerService);
  readonly replayPlayer = inject(ReplayPlayerService);

  readonly actionDrawer = inject(ActionDrawerService);

  /** Below 1600px the action panel leaves the grid and becomes a drawer (guide seção 5.2). */
  readonly isActionDrawer = toSignal(
    this.breakpointObserver.observe('(max-width: 1599px)').pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  readonly traceId = toSignal(this.route.queryParamMap.pipe(map((params) => params.get('trace'))), {
    initialValue: this.route.snapshot.queryParamMap.get('trace'),
  });

  readonly liveTraces = signal<FlowTraceSummary[]>([]);
  readonly liveTracesLoading = signal(false);

  readonly selectedNodeId = signal<string | null>(null);
  readonly selectedSpanDetail = signal<SpanDetail | null>(null);
  readonly slowThresholdMicros = signal<number | null>(null);

  readonly isReplaying = signal(false);

  readonly trace = this.livePlayer.trace;
  readonly graph = computed<GraphResult>(() => {
    const trace = this.trace();
    const slowThresholdMicros = this.slowThresholdMicros();
    if (!trace || slowThresholdMicros === null) {
      return { nodes: [], edges: [] };
    }

    if (this.isReplaying()) {
      return buildGraph({
        spans: this.replayPlayer.visibleSpans(),
        traceStatus: trace.status,
        slowThresholdMicros,
        blueprintSpans: trace.spans,
      });
    }

    return buildGraph({ spans: trace.spans, traceStatus: trace.status, slowThresholdMicros });
  });

  constructor() {
    this.api
      .getConfig()
      .subscribe((config) => this.slowThresholdMicros.set(config.slowThresholdMicros));

    effect(() => {
      this.actionDrawer.isDrawer.set(this.isActionDrawer());
    });

    effect(() => {
      const traceId = this.traceId();
      this.selectedNodeId.set(null);
      this.selectedSpanDetail.set(null);
      this.isReplaying.set(false);

      if (traceId) {
        this.livePlayer.track(traceId);
      } else {
        this.livePlayer.stop();
        this.loadLiveTraces();
      }
    });

    effect(() => {
      const trace = this.trace();
      if (trace) {
        this.replayPlayer.load(trace);
      }
    });
  }

  readonly replaySpeeds = REPLAY_SPEEDS;

  readonly notExecutedCount = computed(
    () => this.graph().nodes.filter((node) => node.state === 'not-executed').length,
  );

  readonly errorOrigin = computed(() => {
    const trace = this.trace();
    return trace?.status === 'Error'
      ? (trace.spans.find((span) => span.isErrorOrigin) ?? null)
      : null;
  });

  readonly slowestStep = computed(() => {
    const trace = this.trace();
    const threshold = this.slowThresholdMicros();
    if (!trace || trace.status === 'Error' || threshold === null) {
      return null;
    }

    return (
      trace.spans
        .filter((span) => (span.durationMicros ?? 0) >= threshold)
        .sort((a, b) => (b.durationMicros ?? 0) - (a.durationMicros ?? 0))[0] ?? null
    );
  });

  readonly replayStatusLine = computed(() => {
    const trace = this.trace();
    if (!trace) {
      return '';
    }

    const total = this.replayPlayer.totalSteps();
    const step = Math.min(this.replayPlayer.stepIndex(), total);
    const speed = this.replayPlayer.speed();

    return this.replayPlayer.state() === 'finished'
      ? this.language.translate('replay.status.finished', {
          step,
          total,
          status: trace.httpStatus ?? '—',
          speed,
        })
      : this.language.translate('replay.status.playing', { step, total, speed });
  });

  methodTagClass(method: string): string {
    return httpMethodTagClass(method);
  }

  shortTraceId(traceId: string): string {
    return traceId.length > 8 ? `${traceId.slice(0, 8)}…` : traceId;
  }

  formatDuration(micros: number | null): string {
    return formatDurationMicros(micros);
  }

  copyTraceId(traceId: string): void {
    void navigator.clipboard?.writeText(traceId);
  }

  openTrace(traceId: string): void {
    this.router.navigate([], { queryParams: { trace: traceId } });
  }

  onNodeSelected(nodeId: string): void {
    this.selectedNodeId.set(nodeId);

    const traceId = this.traceId();
    if (!traceId || isGhostNodeId(nodeId)) {
      this.selectedSpanDetail.set(null);
      return;
    }

    this.api.getSpan(traceId, nodeId).subscribe((detail) => this.selectedSpanDetail.set(detail));
  }

  backToSummary(): void {
    this.selectedNodeId.set(null);
    this.selectedSpanDetail.set(null);
  }

  toggleReplay(): void {
    const trace = this.trace();
    if (!trace) {
      return;
    }

    this.isReplaying.update((replaying) => !replaying);
    if (this.isReplaying()) {
      this.replayPlayer.load(trace);
    }
  }

  setSpeed(speed: number): void {
    this.replayPlayer.setSpeed(speed);
  }

  onSeek(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.replayPlayer.seek(value);
  }

  private loadLiveTraces(): void {
    this.liveTracesLoading.set(true);
    this.api.getLiveTraces().subscribe((traces) => {
      this.liveTraces.set(traces);
      this.liveTracesLoading.set(false);
    });
  }
}
