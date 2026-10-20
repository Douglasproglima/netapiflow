import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { FlowTraceSummary, SpanDetail } from '../../core/models';
import { LiveTracePlayerService } from './playback/live-trace-player.service';
import { buildGraph, GraphResult } from './graph/graph-builder';
import { FlowGraphComponent } from './graph/flow-graph.component';
import { FlowDetailsPanelComponent } from './details/flow-details-panel.component';

const GHOST_ID_PREFIX = 'ghost-';

/**
 * Default screen (`/flow`, D-23): with no `?trace=` query param, lists the traces currently in
 * progress; with one, tracks that trace live (LiveTracePlayerService, tarefa 4.7) and renders its
 * graph and details panel.
 */
@Component({
  selector: 'app-flow-page',
  templateUrl: './flow-page.component.html',
  styleUrl: './flow-page.component.scss',
  imports: [FlowGraphComponent, FlowDetailsPanelComponent],
  providers: [LiveTracePlayerService],
})
export class FlowPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  readonly livePlayer = inject(LiveTracePlayerService);

  readonly traceId = toSignal(this.route.queryParamMap.pipe(map((params) => params.get('trace'))), {
    initialValue: this.route.snapshot.queryParamMap.get('trace'),
  });

  readonly liveTraces = signal<FlowTraceSummary[]>([]);
  readonly liveTracesLoading = signal(false);

  readonly selectedNodeId = signal<string | null>(null);
  readonly selectedSpanDetail = signal<SpanDetail | null>(null);
  private readonly slowThresholdMicros = signal<number | null>(null);

  readonly trace = this.livePlayer.trace;
  readonly graph = computed<GraphResult>(() => {
    const trace = this.trace();
    const slowThresholdMicros = this.slowThresholdMicros();
    return trace && slowThresholdMicros !== null
      ? buildGraph({ spans: trace.spans, traceStatus: trace.status, slowThresholdMicros })
      : { nodes: [], edges: [] };
  });

  constructor() {
    this.api
      .getConfig()
      .subscribe((config) => this.slowThresholdMicros.set(config.slowThresholdMicros));

    effect(() => {
      const traceId = this.traceId();
      this.selectedNodeId.set(null);
      this.selectedSpanDetail.set(null);

      if (traceId) {
        this.livePlayer.track(traceId);
      } else {
        this.livePlayer.stop();
        this.loadLiveTraces();
      }
    });
  }

  openTrace(traceId: string): void {
    this.router.navigate([], { queryParams: { trace: traceId } });
  }

  onNodeSelected(nodeId: string): void {
    this.selectedNodeId.set(nodeId);

    const traceId = this.traceId();
    if (!traceId || nodeId.startsWith(GHOST_ID_PREFIX)) {
      this.selectedSpanDetail.set(null);
      return;
    }

    this.api.getSpan(traceId, nodeId).subscribe((detail) => this.selectedSpanDetail.set(detail));
  }

  private loadLiveTraces(): void {
    this.liveTracesLoading.set(true);
    this.api.getLiveTraces().subscribe((traces) => {
      this.liveTraces.set(traces);
      this.liveTracesLoading.set(false);
    });
  }
}
