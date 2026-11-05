import { Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { ArchitectureMap, EndpointDescriptor } from '../../core/models';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationKey } from '../../core/i18n/translations';
import { PeriodPreset, periodPresetFrom } from '../../shared/period-preset';
import { downloadTextFile } from '../../shared/download-file';
import { FlowGraphComponent } from '../flow/graph/flow-graph.component';
import { buildArchitectureGraph } from './architecture-graph-builder';
import { architectureMapToMermaid } from './architecture-mermaid';

const WINDOWS: readonly PeriodPreset[] = ['24h', '7d', '30d'];
const DEFAULT_WINDOW: PeriodPreset = '7d';

function routeKey(method: string, routeTemplate: string): string {
  return `${method.toUpperCase()} ${routeTemplate}`;
}

function inputValue(event: Event): string {
  return (event.target as HTMLSelectElement).value;
}

/**
 * Generalizes /blueprint (last successful execution of a route) to the union of every
 * execution of a route within a window (D-83): same question ("what does this route normally
 * do"), answered from GET /architecture instead of a single trace.
 */
@Component({
  selector: 'app-architecture',
  templateUrl: './architecture.component.html',
  styleUrl: './architecture.component.scss',
  imports: [TranslatePipe, FlowGraphComponent],
})
export class ArchitectureComponent {
  private readonly api = inject(ApiService);

  readonly windows = WINDOWS;
  readonly window = signal<PeriodPreset>(DEFAULT_WINDOW);

  readonly endpoints = signal<EndpointDescriptor[]>([]);
  readonly selectedRouteKey = signal<string | null>(null);

  readonly loading = signal(false);
  readonly error = signal<TranslationKey | null>(null);
  readonly map = signal<ArchitectureMap | null>(null);

  private readonly graphHost = viewChild<ElementRef<HTMLElement>>('graphHost');

  private readonly endpointsByKey = computed(
    () =>
      new Map(
        this.endpoints().map((endpoint) => [
          routeKey(endpoint.method, endpoint.routeTemplate),
          endpoint,
        ]),
      ),
  );

  readonly selectedRoute = computed(() => {
    const key = this.selectedRouteKey();
    return key ? (this.endpointsByKey().get(key) ?? null) : null;
  });

  readonly graph = computed(() =>
    this.map() ? buildArchitectureGraph(this.map()!) : { nodes: [], edges: [] },
  );

  constructor() {
    this.api.getEndpoints().subscribe((endpoints) => this.endpoints.set(endpoints));

    effect(() => {
      const route = this.selectedRoute();
      const window = this.window();

      if (!route) {
        this.map.set(null);
        this.loading.set(false);
        this.error.set(null);
        return;
      }

      const from = periodPresetFrom(window, new Date());
      this.loading.set(true);
      this.error.set(null);

      this.api.getArchitecture(route.method, route.routeTemplate, { from }).subscribe({
        next: (map) => {
          this.map.set(map);
          this.loading.set(false);
        },
        error: () => {
          this.map.set(null);
          this.error.set('architecture.error');
          this.loading.set(false);
        },
      });
    });
  }

  selectRoute(event: Event): void {
    const value = inputValue(event);
    this.selectedRouteKey.set(value || null);
  }

  selectWindow(window: PeriodPreset): void {
    this.window.set(window);
  }

  windowLabelKey(window: PeriodPreset): TranslationKey {
    return `architecture.window.${window}` as TranslationKey;
  }

  routeKeyOf(endpoint: EndpointDescriptor): string {
    return routeKey(endpoint.method, endpoint.routeTemplate);
  }

  trackByEndpoint(_index: number, endpoint: EndpointDescriptor): string {
    return routeKey(endpoint.method, endpoint.routeTemplate);
  }

  exportMermaid(): void {
    const map = this.map();
    if (!map) {
      return;
    }

    downloadTextFile(architectureMapToMermaid(map), 'architecture.mmd', 'text/plain');
  }

  exportSvg(): void {
    const svg = this.graphHost()?.nativeElement.querySelector('svg');
    if (!svg) {
      return;
    }

    const clone = svg.cloneNode(true) as SVGSVGElement;
    if (!clone.getAttribute('xmlns')) {
      clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
    }

    downloadTextFile(
      new XMLSerializer().serializeToString(clone),
      'architecture.svg',
      'image/svg+xml',
    );
  }
}
