import { Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, firstValueFrom, of, switchMap } from 'rxjs';
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

const INLINED_PRESENTATION_PROPERTIES = ['fill', 'stroke', 'color', 'font-family'] as const;

/**
 * Bakes each element's own resolved color/font (M7) straight onto its exported clone, in the
 * same traversal order `cloneNode(true)` preserves, so the exported file keeps looking the same
 * with no dashboard stylesheet around to resolve `var(--naf-*)` through.
 */
function inlineComputedPresentation(live: Element, cloned: Element): void {
  const liveElements = [live, ...Array.from(live.querySelectorAll('*'))];
  const clonedElements = [cloned, ...Array.from(cloned.querySelectorAll('*'))];

  liveElements.forEach((liveElement, index) => {
    const clonedElement = clonedElements[index];
    if (!(clonedElement instanceof SVGElement)) {
      return;
    }

    const computed = getComputedStyle(liveElement);
    for (const property of INLINED_PRESENTATION_PROPERTIES) {
      const value = computed.getPropertyValue(property);
      if (value) {
        clonedElement.style.setProperty(property, value);
      }
    }
  });
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
  readonly tailSamplingEnabled = signal(false);
  readonly successSampleRate = signal(0);

  readonly tailSamplingRateLabel = computed(() => `${Math.round(this.successSampleRate() * 100)}%`);

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

  private readonly architectureTrigger$ = new Subject<{
    method: string;
    routeTemplate: string;
    from: string;
  }>();

  constructor() {
    this.api.getEndpoints().subscribe((endpoints) => this.endpoints.set(endpoints));

    this.api.getConfig().subscribe((config) => {
      this.tailSamplingEnabled.set(config.tailSamplingEnabled);
      this.successSampleRate.set(config.successSampleRate);
    });

    // `switchMap` (M4) cancels a still-in-flight request for an earlier route/window
    // combination the moment a new one starts, so its response can never arrive after and
    // overwrite it.
    this.architectureTrigger$
      .pipe(
        switchMap(({ method, routeTemplate, from }) =>
          this.api.getArchitecture(method, routeTemplate, { from }).pipe(
            catchError(() => {
              this.map.set(null);
              this.error.set('architecture.error');
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((map) => {
        if (map) {
          this.map.set(map);
        }
        this.loading.set(false);
      });

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
      this.architectureTrigger$.next({
        method: route.method,
        routeTemplate: route.routeTemplate,
        from,
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

  async exportSvg(): Promise<void> {
    const svg = this.graphHost()?.nativeElement.querySelector('svg');
    if (!svg) {
      return;
    }

    const clone = svg.cloneNode(true) as SVGSVGElement;
    if (!clone.getAttribute('xmlns')) {
      clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
    }

    inlineComputedPresentation(svg, clone);
    await this.inlineStateIcons(clone);

    downloadTextFile(
      new XMLSerializer().serializeToString(clone),
      'architecture.svg',
      'image/svg+xml',
    );
  }

  /**
   * Replaces each state icon's relative `href` with the icon's own markup as a data URI (M7), so
   * the exported file keeps showing every node's icon when opened on its own, with no dashboard
   * server or stylesheet around to resolve the original relative path.
   */
  private async inlineStateIcons(svg: SVGSVGElement): Promise<void> {
    const images = Array.from(svg.querySelectorAll('image'));
    const hrefs = [
      ...new Set(
        images
          .map((image) => image.getAttribute('href'))
          .filter((href): href is string => !!href && !href.startsWith('data:')),
      ),
    ];

    const dataUriByHref = new Map(
      await Promise.all(
        hrefs.map(async (href) => {
          const markup = await firstValueFrom(this.api.getStaticAsset(href));
          return [href, `data:image/svg+xml;utf8,${encodeURIComponent(markup)}`] as const;
        }),
      ),
    );

    for (const image of images) {
      const dataUri = dataUriByHref.get(image.getAttribute('href') ?? '');
      if (dataUri) {
        image.setAttribute('href', dataUri);
      }
    }
  }
}
