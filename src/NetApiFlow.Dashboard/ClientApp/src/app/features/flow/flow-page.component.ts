import { Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { BreakpointObserver } from '@angular/cdk/layout';
import { Subject, auditTime, catchError, filter, map, merge, of, switchMap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { ActionDrawerService } from '../../core/action-drawer.service';
import { ApiService } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import {
  FlowSpan,
  FlowTraceDetail,
  FlowTraceSummary,
  SpanDetail,
  UNMATCHED_ROUTE_TEMPLATE,
} from '../../core/models';
import { SSE_REFRESH_AUDIT_MS, SseService } from '../../core/sse.service';
import { LiveTracePlayerService } from './playback/live-trace-player.service';
import { ReplayPlayerService } from './playback/replay-player.service';
import { buildGraph, GraphResult, isGhostNodeId } from './graph/graph-builder';
import { FlowGraphComponent } from './graph/flow-graph.component';
import { rankdirHysteresis } from './graph/rankdir-hysteresis';
import { FlowDetailsPanelComponent } from './details/flow-details-panel.component';
import { MetricCardsComponent } from './metric-cards/metric-cards.component';
import { EventFeedComponent } from './event-feed/event-feed.component';
import { RecentExecutionsComponent } from './recent-executions/recent-executions.component';
import { EndpointFormComponent } from '../runner/endpoint-form.component';
import { httpMethodTagClass } from '../../shared/http-method-style';
import { formatDurationMicros } from '../../shared/duration-format';
import { downloadTextFile } from '../../shared/download-file';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslationKey } from '../../core/i18n/translations';
import { parseTraceFile, traceExportFileName } from './trace-file';
import { buildLocalSpanDetail } from './details/local-span-detail';

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
    MatMenuModule,
    TranslatePipe,
  ],
  providers: [LiveTracePlayerService, ReplayPlayerService],
})
export class FlowPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  private readonly config = inject(ConfigService);
  private readonly sse = inject(SseService);
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

  private readonly canvasRef = viewChild<ElementRef<HTMLElement>>('canvas');
  readonly graphRankdir = rankdirHysteresis(this.canvasRef);

  /**
   * Below ~800px of `window.innerHeight` (guide seção 6.9) the footer shows one card at a time
   * instead of two side by side; this picks which. Purely a display choice (CSS hides the
   * inactive card inside that same media query), with no effect above the threshold, where both
   * always show.
   */
  readonly activeFooterTab = signal<'recent' | 'events'>('recent');

  /** Lifted from each card's own `collapsed` model so the shared footer row can shrink. */
  readonly recentCollapsed = signal(false);
  readonly feedCollapsed = signal(false);

  readonly traceId = toSignal(this.route.queryParamMap.pipe(map((params) => params.get('trace'))), {
    initialValue: this.route.snapshot.queryParamMap.get('trace'),
  });

  readonly liveTraces = signal<FlowTraceSummary[]>([]);
  readonly liveTracesLoading = signal(false);
  readonly liveTracesError = signal(false);

  readonly selectedNodeId = signal<string | null>(null);
  readonly selectedSpanDetail = signal<SpanDetail | null>(null);
  readonly slowThresholdMicros = signal<number | null>(null);

  readonly isReplaying = signal(false);

  readonly importedTrace = signal<FlowTraceDetail | null>(null);
  readonly importErrorKey = signal<TranslationKey | null>(null);

  protected readonly unmatchedRouteTemplate = UNMATCHED_ROUTE_TEMPLATE;

  readonly trace = computed<FlowTraceDetail | null>(
    () => this.importedTrace() ?? this.livePlayer.trace(),
  );

  readonly traceNotFound = computed(
    () => this.traceId() !== null && !this.importedTrace() && this.livePlayer.notFound(),
  );

  /**
   * The last successful execution of the live trace's own (method, route), fetched once per
   * tracked trace (M1) so a live trace can mark a branch its blueprint predicted but that hasn't
   * shown up as "not-executed" the same way a replay already can, instead of only ever reporting
   * what has executed so far.
   */
  private readonly liveBlueprintSpans = signal<FlowSpan[] | null>(null);
  private liveBlueprintTraceId: string | null = null;

  readonly graph = computed<GraphResult>(() => {
    const trace = this.trace();
    const slowThresholdMicros = this.slowThresholdMicros();
    if (!trace || slowThresholdMicros === null) {
      return { nodes: [], edges: [] };
    }

    const rankdir = this.graphRankdir();

    if (this.isReplaying()) {
      return buildGraph({
        spans: this.replayPlayer.visibleSpans(),
        traceStatus: trace.status,
        slowThresholdMicros,
        fullSpans: trace.spans,
        inProgress: this.replayPlayer.state() !== 'finished',
        rankdir,
      });
    }

    return buildGraph({
      spans: trace.spans,
      traceStatus: trace.status,
      slowThresholdMicros,
      blueprintSpans: this.liveBlueprintSpans() ?? undefined,
      inProgress: trace.status === 'Running',
      rankdir,
    });
  });

  private readonly spanSelection$ = new Subject<{ traceId: string; nodeId: string }>();
  private readonly liveTracesTrigger$ = new Subject<void>();

  constructor() {
    this.config.config$.subscribe((config) =>
      this.slowThresholdMicros.set(config.slowThresholdMicros),
    );

    // `switchMap` (M4) cancels a still-in-flight span lookup for an earlier selection the
    // moment a new one starts, so its response can never arrive after and overwrite it.
    this.spanSelection$
      .pipe(
        switchMap(({ traceId, nodeId }) => this.api.getSpan(traceId, nodeId)),
        takeUntilDestroyed(),
      )
      .subscribe((detail) => this.selectedSpanDetail.set(detail));

    // Same `auditTime` coalescing as the topbar's in-progress count (M6): a burst of
    // TraceStarted/TraceCompleted events refreshes the list at most once per window, only while
    // it is the one actually shown (no trace selected).
    merge(
      this.liveTracesTrigger$,
      this.sse.events$.pipe(
        filter(
          (event) =>
            this.traceId() === null &&
            (event.type === 'TraceStarted' || event.type === 'TraceCompleted'),
        ),
        auditTime(SSE_REFRESH_AUDIT_MS),
      ),
    )
      .pipe(
        switchMap(() =>
          this.api.getLiveTraces().pipe(
            catchError(() => {
              this.liveTracesError.set(true);
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((traces) => {
        if (traces) {
          this.liveTraces.set(traces);
        }
        this.liveTracesLoading.set(false);
      });

    effect(() => {
      this.actionDrawer.isDrawer.set(this.isActionDrawer());
    });

    effect(() => {
      const traceId = this.traceId();
      this.selectedNodeId.set(null);
      this.selectedSpanDetail.set(null);
      this.isReplaying.set(false);
      this.liveBlueprintSpans.set(null);
      this.liveBlueprintTraceId = null;

      if (traceId) {
        this.importedTrace.set(null);
        this.importErrorKey.set(null);
        this.livePlayer.track(traceId);
      } else {
        this.livePlayer.stop();
        this.liveTracesLoading.set(true);
        this.liveTracesError.set(false);
        this.liveTracesTrigger$.next();
      }
    });

    effect(() => {
      const trace = this.livePlayer.trace();
      if (!trace || trace.traceId === this.liveBlueprintTraceId) {
        return;
      }

      this.liveBlueprintTraceId = trace.traceId;
      if (trace.routeTemplate === UNMATCHED_ROUTE_TEMPLATE || trace.status === 'Running') {
        return;
      }

      this.api.getBlueprint(trace.method, trace.routeTemplate, trace.startedAt).subscribe({
        next: (blueprint) => this.applyBlueprint(trace.traceId, blueprint?.spans ?? null),
        error: () => this.applyBlueprint(trace.traceId, null),
      });
    });

    // Keyed by traceId plus whether the trace is still Running (M5), so a live trace's growing
    // span list never reloads an already-playing replay back to step 0: only a new trace
    // identity or the trace concluding is a real reason to reload.
    effect(() => {
      const trace = this.trace();
      if (!trace) {
        return;
      }

      const loadKey = `${trace.traceId}:${trace.status === 'Running'}`;
      if (loadKey === this.lastReplayLoadKey) {
        return;
      }

      this.lastReplayLoadKey = loadKey;
      this.replayPlayer.load(trace);
    });
  }

  private lastReplayLoadKey: string | null = null;

  private applyBlueprint(traceId: string, spans: FlowSpan[] | null): void {
    if (traceId === this.liveBlueprintTraceId) {
      this.liveBlueprintSpans.set(spans);
    }
  }

  readonly replaySpeeds = REPLAY_SPEEDS;

  readonly notExecutedCount = computed(
    () => this.graph().nodes.filter((node) => node.state === 'not-executed').length,
  );

  readonly isFollowingLiveRunning = computed(
    () =>
      !this.importedTrace() && !this.isReplaying() && this.livePlayer.trace()?.status === 'Running',
  );

  readonly errorOrigin = computed(() => {
    const trace = this.trace();
    return trace?.status === 'Error' || trace?.status === 'Timeout'
      ? (trace.spans.find((span) => span.isErrorOrigin) ?? null)
      : null;
  });

  readonly slowestStep = computed(() => {
    const trace = this.trace();
    const threshold = this.slowThresholdMicros();
    if (!trace || trace.status === 'Error' || trace.status === 'Timeout' || threshold === null) {
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

  exportTrace(): void {
    const trace = this.trace();
    if (!trace) {
      return;
    }

    downloadTextFile(
      JSON.stringify(trace, null, 2),
      traceExportFileName(trace.traceId),
      'application/json',
    );
  }

  async onImportFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (!file) {
      return;
    }

    let raw: string;
    try {
      raw = await file.text();
    } catch {
      this.importErrorKey.set('import.error.readFailed');
      return;
    }

    this.applyImportedTraceFile(raw);
  }

  exitImportedTrace(): void {
    this.importedTrace.set(null);
    this.importErrorKey.set(null);
  }

  onNodeSelected(nodeId: string): void {
    this.selectedNodeId.set(nodeId);

    if (isGhostNodeId(nodeId)) {
      this.selectedSpanDetail.set(null);
      return;
    }

    const importedTrace = this.importedTrace();
    if (importedTrace) {
      this.selectedSpanDetail.set(buildLocalSpanDetail(importedTrace, nodeId));
      return;
    }

    const traceId = this.traceId();
    if (!traceId) {
      this.selectedSpanDetail.set(null);
      return;
    }

    this.spanSelection$.next({ traceId, nodeId });
  }

  backToSummary(): void {
    this.selectedNodeId.set(null);
    this.selectedSpanDetail.set(null);
  }

  toggleLivePause(): void {
    if (this.livePlayer.paused()) {
      this.livePlayer.resume();
    } else {
      this.livePlayer.pause();
    }
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

  private applyImportedTraceFile(raw: string): void {
    const result = parseTraceFile(raw);
    if (!result.ok) {
      this.importErrorKey.set(
        result.reason === 'invalid-json' ? 'import.error.invalidJson' : 'import.error.invalidShape',
      );
      return;
    }

    this.importErrorKey.set(null);
    this.selectedNodeId.set(null);
    this.selectedSpanDetail.set(null);
    this.isReplaying.set(false);
    this.importedTrace.set(result.trace);

    if (this.traceId()) {
      this.router.navigate([], { queryParams: {} });
    }
  }
}
