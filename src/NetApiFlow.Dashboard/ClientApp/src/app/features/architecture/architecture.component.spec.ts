import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ArchitectureComponent } from './architecture.component';
import { ArchitectureMap, DashboardConfig, EndpointDescriptor } from '../../core/models';

const ENDPOINTS: EndpointDescriptor[] = [
  { method: 'GET', routeTemplate: '/api/orders/{id}', displayName: 'GET /api/orders/{id}' },
];

const CONFIG: DashboardConfig = {
  slowThresholdMicros: 250_000,
  version: '0.1.0',
  tailSamplingEnabled: false,
  successSampleRate: 0.1,
  openApiDocumentPath: '/openapi/v1.json',
};

function selectRoute(
  fixture: ComponentFixture<ArchitectureComponent>,
  method: string,
  route: string,
): void {
  fixture.componentInstance.selectRoute({
    target: { value: `${method} ${route}` },
  } as unknown as Event);
}

async function selectRouteWithGraph(
  fixture: ComponentFixture<ArchitectureComponent>,
  httpMock: HttpTestingController,
): Promise<void> {
  selectRoute(fixture, 'GET', '/api/orders/{id}');
  await fixture.whenStable();

  const singleNodeMap: ArchitectureMap = {
    totalExecutions: 1,
    nodes: [
      {
        component: 'OrdersController',
        operation: 'GetOrder',
        type: 'Controller',
        executionCount: 1,
      },
    ],
    edges: [],
  };
  httpMock.expectOne((req) => req.url === 'api/architecture').flush(singleNodeMap);
  await fixture.whenStable();
}

describe('ArchitectureComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('prompts to select a route before any route is chosen', async () => {
    const fixture = TestBed.createComponent(ArchitectureComponent);
    await fixture.whenStable();
    httpMock.expectOne((req) => req.url === 'api/endpoints').flush(ENDPOINTS);
    httpMock.expectOne((req) => req.url === 'api/config').flush(CONFIG);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain(
      'Selecione uma rota para ver o grafo de chamadas.',
    );
  });

  it('shows the tail-sampling banner when the backend reports it enabled', async () => {
    const fixture = TestBed.createComponent(ArchitectureComponent);
    await fixture.whenStable();
    httpMock.expectOne((req) => req.url === 'api/endpoints').flush(ENDPOINTS);
    httpMock
      .expectOne((req) => req.url === 'api/config')
      .flush({ ...CONFIG, tailSamplingEnabled: true, successSampleRate: 0.1 });
    await fixture.whenStable();

    const banner = fixture.nativeElement.querySelector('.architecture__tail-sampling-banner');
    expect(banner).not.toBeNull();
    expect(banner!.textContent).toContain('10%');
  });

  it('hides the tail-sampling banner when the backend reports it disabled', async () => {
    const fixture = TestBed.createComponent(ArchitectureComponent);
    await fixture.whenStable();
    httpMock.expectOne((req) => req.url === 'api/endpoints').flush(ENDPOINTS);
    httpMock
      .expectOne((req) => req.url === 'api/config')
      .flush({ ...CONFIG, tailSamplingEnabled: false });
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.architecture__tail-sampling-banner')).toBeNull();
  });

  it('shows an explicit empty state when the selected route had no execution in the window', async () => {
    const fixture = TestBed.createComponent(ArchitectureComponent);
    await fixture.whenStable();
    httpMock.expectOne((req) => req.url === 'api/endpoints').flush(ENDPOINTS);
    httpMock.expectOne((req) => req.url === 'api/config').flush(CONFIG);
    await fixture.whenStable();

    selectRoute(fixture, 'GET', '/api/orders/{id}');
    await fixture.whenStable();

    const emptyMap: ArchitectureMap = { totalExecutions: 0, nodes: [], edges: [] };
    httpMock.expectOne((req) => req.url === 'api/architecture').flush(emptyMap);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain(
      'Nenhuma execução registrada para esta rota nesta janela.',
    );
  });

  it('shows an explicit error state when the request fails', async () => {
    const fixture = TestBed.createComponent(ArchitectureComponent);
    await fixture.whenStable();
    httpMock.expectOne((req) => req.url === 'api/endpoints').flush(ENDPOINTS);
    httpMock.expectOne((req) => req.url === 'api/config').flush(CONFIG);
    await fixture.whenStable();

    selectRoute(fixture, 'GET', '/api/orders/{id}');
    await fixture.whenStable();

    httpMock
      .expectOne((req) => req.url === 'api/architecture')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Falha ao carregar o mapa de arquitetura.');
  });

  it('refetches with the expected method/route/from when the route or the window changes', async () => {
    const fixture = TestBed.createComponent(ArchitectureComponent);
    await fixture.whenStable();
    httpMock.expectOne((req) => req.url === 'api/endpoints').flush(ENDPOINTS);
    httpMock.expectOne((req) => req.url === 'api/config').flush(CONFIG);
    await fixture.whenStable();

    selectRoute(fixture, 'GET', '/api/orders/{id}');
    await fixture.whenStable();

    const firstReq = httpMock.expectOne((req) => req.url === 'api/architecture');
    expect(firstReq.request.params.get('method')).toBe('GET');
    expect(firstReq.request.params.get('route')).toBe('/api/orders/{id}');
    firstReq.flush({
      totalExecutions: 1,
      nodes: [
        {
          component: 'OrdersController',
          operation: 'GetOrder',
          type: 'Controller',
          executionCount: 1,
        },
      ],
      edges: [],
    } satisfies ArchitectureMap);
    await fixture.whenStable();

    fixture.componentInstance.selectWindow('24h');
    await fixture.whenStable();

    const secondReq = httpMock.expectOne((req) => req.url === 'api/architecture');
    const from = new Date(secondReq.request.params.get('from') as string);
    const hoursAgo = (Date.now() - from.getTime()) / (60 * 60 * 1000);
    expect(hoursAgo).toBeGreaterThan(23);
    expect(hoursAgo).toBeLessThan(25);
    secondReq.flush({ totalExecutions: 0, nodes: [], edges: [] } satisfies ArchitectureMap);
  });

  it('always reflects the latest route, never a stale response that arrives after a newer one (M4)', async () => {
    const fixture = TestBed.createComponent(ArchitectureComponent);
    await fixture.whenStable();
    httpMock
      .expectOne((req) => req.url === 'api/endpoints')
      .flush([
        ...ENDPOINTS,
        { method: 'POST', routeTemplate: '/api/orders', displayName: 'POST /api/orders' },
      ]);
    httpMock.expectOne((req) => req.url === 'api/config').flush(CONFIG);
    await fixture.whenStable();

    selectRoute(fixture, 'GET', '/api/orders/{id}');
    await fixture.whenStable();
    const firstReq = httpMock.expectOne((req) => req.url === 'api/architecture');

    selectRoute(fixture, 'POST', '/api/orders');
    await fixture.whenStable();

    // `switchMap` (M4) unsubscribes the first (now stale) route's lookup the moment the
    // second route's request starts.
    expect(firstReq.cancelled).toBe(true);

    const secondReq = httpMock.expectOne((req) => req.url === 'api/architecture');
    expect(secondReq.request.params.get('route')).toBe('/api/orders');
    const secondMap: ArchitectureMap = {
      totalExecutions: 1,
      nodes: [
        {
          component: 'OrdersController',
          operation: 'Create',
          type: 'Controller',
          executionCount: 1,
        },
      ],
      edges: [],
    };
    secondReq.flush(secondMap);
    await fixture.whenStable();

    expect(fixture.componentInstance.map()).toEqual(secondMap);
  });

  it('exportSvg serializes the rendered graph and downloads it with the svg MIME type', async () => {
    const fixture = TestBed.createComponent(ArchitectureComponent);
    await fixture.whenStable();
    httpMock.expectOne((req) => req.url === 'api/endpoints').flush(ENDPOINTS);
    httpMock.expectOne((req) => req.url === 'api/config').flush(CONFIG);
    await fixture.whenStable();
    await selectRouteWithGraph(fixture, httpMock);

    const createObjectURL = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url');
    const revokeObjectURL = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => undefined);

    const exportPromise = fixture.componentInstance.exportSvg();
    httpMock.expectOne('node-states/completed.svg').flush('<svg><circle/></svg>');
    await exportPromise;

    expect(createObjectURL).toHaveBeenCalledTimes(1);
    expect((createObjectURL.mock.calls[0][0] as Blob).type).toBe('image/svg+xml');
    expect(click).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock-url');

    createObjectURL.mockRestore();
    revokeObjectURL.mockRestore();
    click.mockRestore();
  });

  it('exports the SVG self-contained (M7): no relative icon href, each element carries its resolved color inline', async () => {
    const fixture = TestBed.createComponent(ArchitectureComponent);
    await fixture.whenStable();
    httpMock.expectOne((req) => req.url === 'api/endpoints').flush(ENDPOINTS);
    httpMock.expectOne((req) => req.url === 'api/config').flush(CONFIG);
    await fixture.whenStable();
    await selectRouteWithGraph(fixture, httpMock);

    const createObjectURL = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url');
    const revokeObjectURL = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => undefined);
    const getComputedStyleSpy = vi.spyOn(window, 'getComputedStyle').mockReturnValue({
      getPropertyValue: (property: string) => (property === 'fill' ? 'rgb(1, 2, 3)' : ''),
    } as CSSStyleDeclaration);

    const exportPromise = fixture.componentInstance.exportSvg();
    httpMock
      .expectOne('node-states/completed.svg')
      .flush('<svg xmlns="http://www.w3.org/2000/svg"><circle fill="#10B981"/></svg>');
    await exportPromise;

    const exportedMarkup = await (createObjectURL.mock.calls[0][0] as Blob).text();

    expect(exportedMarkup).not.toContain('node-states/completed.svg');
    expect(exportedMarkup).toContain('data:image/svg+xml');
    expect(exportedMarkup).toContain('fill: rgb(1, 2, 3)');

    getComputedStyleSpy.mockRestore();
    createObjectURL.mockRestore();
    revokeObjectURL.mockRestore();
    click.mockRestore();
  });

  it('exportMermaid downloads the generated flowchart with a text MIME type', async () => {
    const fixture = TestBed.createComponent(ArchitectureComponent);
    await fixture.whenStable();
    httpMock.expectOne((req) => req.url === 'api/endpoints').flush(ENDPOINTS);
    httpMock.expectOne((req) => req.url === 'api/config').flush(CONFIG);
    await fixture.whenStable();
    await selectRouteWithGraph(fixture, httpMock);

    const createObjectURL = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url');
    const revokeObjectURL = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => undefined);

    fixture.componentInstance.exportMermaid();

    expect(createObjectURL).toHaveBeenCalledTimes(1);
    expect((createObjectURL.mock.calls[0][0] as Blob).type).toBe('text/plain');
    expect(click).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock-url');

    createObjectURL.mockRestore();
    revokeObjectURL.mockRestore();
    click.mockRestore();
  });
});
