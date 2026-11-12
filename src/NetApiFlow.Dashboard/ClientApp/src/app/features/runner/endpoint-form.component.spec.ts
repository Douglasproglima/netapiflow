import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { EndpointFormComponent } from './endpoint-form.component';
import { OpenApiDocument } from '../../core/models';

const DOC: OpenApiDocument = {
  paths: {
    '/api/orders/{id}': {
      post: {
        operationId: 'CreateOrder',
        parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'integer' } }],
        requestBody: {
          required: true,
          content: { 'application/json': { schema: { type: 'object' } } },
        },
      },
    },
  },
};

describe('EndpointFormComponent', () => {
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

  it('builds the expected form for an endpoint with a path param and a body', async () => {
    const fixture = TestBed.createComponent(EndpointFormComponent);
    await fixture.whenStable();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
    await fixture.whenStable();

    expect(fixture.componentInstance.form()).toEqual({
      method: 'POST',
      path: '/api/orders/{id}',
      operationId: 'CreateOrder',
      hasBody: true,
      fields: [
        { name: 'id', kind: 'path', type: 'integer', required: true },
        { name: 'body', kind: 'body', type: 'object', required: true },
      ],
    });
  });

  it('shows an explicit error state when the OpenAPI document fails to load', async () => {
    const fixture = TestBed.createComponent(EndpointFormComponent);
    await fixture.whenStable();
    httpMock
      .expectOne('/openapi/v1.json')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    // pt-BR is the default language (D-35).
    expect(fixture.nativeElement.textContent).toContain(
      'Não foi possível carregar o documento OpenAPI.',
    );
  });

  it('filters the endpoint list by route', async () => {
    const fixture = TestBed.createComponent(EndpointFormComponent);
    await fixture.whenStable();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.setRouteFilter('orders');
    expect(fixture.componentInstance.endpoints()).toHaveLength(1);

    fixture.componentInstance.setRouteFilter('nothing-matches');
    expect(fixture.componentInstance.endpoints()).toHaveLength(0);
  });

  it('dispatches the filled form and emits the returned trace id', async () => {
    const fixture = TestBed.createComponent(EndpointFormComponent);
    await fixture.whenStable();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    let dispatchedTraceId: string | null = null;
    fixture.componentInstance.traceDispatched.subscribe((traceId) => (dispatchedTraceId = traceId));

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
    fixture.componentInstance.setFieldValue('id', '5');
    fixture.componentInstance.setFieldValue('body', '{"customerName":"Acme"}');
    fixture.componentInstance.send();
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/orders/5');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ customerName: 'Acme' });
    req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't1' } });
    await fixture.whenStable();

    expect(fixture.componentInstance.lastTraceId()).toBe('t1');
    expect(dispatchedTraceId).toBe('t1');
  });

  it('switches to the Scenarios tab and dispatches a scenario without a delay header by default', async () => {
    const fixture = TestBed.createComponent(EndpointFormComponent);
    await fixture.whenStable();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    expect(fixture.componentInstance.activeTab()).toBe('custom');
    fixture.componentInstance.setActiveTab('scenarios');
    expect(fixture.componentInstance.activeTab()).toBe('scenarios');

    const [successScenario] = fixture.componentInstance.scenarios;
    fixture.componentInstance.sendScenario(successScenario);
    await fixture.whenStable();

    const req = httpMock.expectOne(successScenario.path);
    expect(req.request.method).toBe(successScenario.method);
    expect(req.request.headers.has('X-NetApiFlow-Demo-Delay-Ms')).toBe(false);
    req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't2' } });
    await fixture.whenStable();

    expect(fixture.componentInstance.lastTraceId()).toBe('t2');
  });

  it('attaches the selected artificial delay header to the dispatched scenario', async () => {
    const fixture = TestBed.createComponent(EndpointFormComponent);
    await fixture.whenStable();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.setActiveTab('scenarios');
    fixture.componentInstance.selectDelay(1000);

    const [successScenario] = fixture.componentInstance.scenarios;
    fixture.componentInstance.sendScenario(successScenario);
    await fixture.whenStable();

    const req = httpMock.expectOne(successScenario.path);
    expect(req.request.headers.get('X-NetApiFlow-Demo-Delay-Ms')).toBe('1000');
    req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't3' } });
    await fixture.whenStable();
  });

  it('still captures the trace id from the Error scenario, which always fails', async () => {
    const fixture = TestBed.createComponent(EndpointFormComponent);
    await fixture.whenStable();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.setActiveTab('scenarios');
    const errorScenario = fixture.componentInstance.scenarios.find((s) => s.key === 'error')!;
    fixture.componentInstance.sendScenario(errorScenario);
    await fixture.whenStable();

    const req = httpMock.expectOne(errorScenario.path);
    req.flush('boom', {
      status: 500,
      statusText: 'Server Error',
      headers: { 'X-NetApiFlow-TraceId': 't4' },
    });
    await fixture.whenStable();

    expect(fixture.componentInstance.lastTraceId()).toBe('t4');
    expect(fixture.componentInstance.sendError()).toBeNull();
  });
});
