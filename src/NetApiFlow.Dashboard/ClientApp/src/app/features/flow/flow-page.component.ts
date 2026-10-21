import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
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

/**
 * Default screen (`/flow`, D-23): a dense workspace (ADR-0010) — metric cards always visible on
 * top, the action panel (shared `EndpointFormComponent`) on the left, the graph with replay
 * controls at the center, the details panel on the right, and the live event feed at the bottom.
 * With no `?trace=` query param the center column lists the traces currently in progress; with
 * one, it tracks that trace live (`LiveTracePlayerService`, tarefa 4.7) or replays it
 * (`ReplayPlayerService`, tarefa 4.9), rendering its graph and details panel either way.
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
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  providers: [LiveTracePlayerService, ReplayPlayerService],
})
export class FlowPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  readonly livePlayer = inject(LiveTracePlayerService);
  readonly replayPlayer = inject(ReplayPlayerService);

  readonly traceId = toSignal(this.route.queryParamMap.pipe(map((params) => params.get('trace'))), {
    initialValue: this.route.snapshot.queryParamMap.get('trace'),
  });

  readonly liveTraces = signal<FlowTraceSummary[]>([]);
  readonly liveTracesLoading = signal(false);

  readonly selectedNodeId = signal<string | null>(null);
  readonly selectedSpanDetail = signal<SpanDetail | null>(null);
  private readonly slowThresholdMicros = signal<number | null>(null);

  readonly isReplaying = signal(false);

  readonly trace = this.livePlayer.trace;
  readonly graph = computed<GraphResult>(() => {
    const trace = this.trace();
    const slowThresholdMicros = this.slowThresholdMicros();
    if (!trace || slowThresholdMicros === null) {
      return { nodes: [], edges: [] };
    }

    const spans = this.isReplaying() ? this.replayPlayer.visibleSpans() : trace.spans;
    return buildGraph({ spans, traceStatus: trace.status, slowThresholdMicros });
  });

  constructor() {
    this.api
      .getConfig()
      .subscribe((config) => this.slowThresholdMicros.set(config.slowThresholdMicros));

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

  methodTagClass(method: string): string {
    return httpMethodTagClass(method);
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

  onSpeedChange(event: Event): void {
    const value = Number((event.target as HTMLSelectElement).value);
    this.replayPlayer.setSpeed(value);
  }

  private loadLiveTraces(): void {
    this.liveTracesLoading.set(true);
    this.api.getLiveTraces().subscribe((traces) => {
      this.liveTraces.set(traces);
      this.liveTracesLoading.set(false);
    });
  }
}
