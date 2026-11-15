import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { EndpointFormComponent } from './endpoint-form.component';
import { DashboardConfig, OpenApiDocument } from '../../core/models';

const CONFIG: DashboardConfig = {
  slowThresholdMicros: 250_000,
  version: '0.1.0',
  tailSamplingEnabled: false,
  successSampleRate: 0.1,
  openApiDocumentPath: '/openapi/v1.json',
};

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

  async function createComponentWithConfig(
    config: Partial<DashboardConfig> = {},
  ): Promise<ComponentFixture<EndpointFormComponent>> {
    const fixture = TestBed.createComponent(EndpointFormComponent);
    await fixture.whenStable();
    httpMock.expectOne('api/config').flush({ ...CONFIG, ...config });
    await fixture.whenStable();
    return fixture;
  }

  it('builds the expected form for an endpoint with a path param and a body', async () => {
    const fixture = await createComponentWithConfig();
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
    const fixture = await createComponentWithConfig();
    httpMock
      .expectOne('/openapi/v1.json')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    // pt-BR is the default language.
    expect(fixture.nativeElement.textContent).toContain(
      'Não foi possível carregar o documento OpenAPI.',
    );
  });

  it('reads the OpenAPI document from the path the host configured', async () => {
    const fixture = await createComponentWithConfig({
      openApiDocumentPath: '/swagger/v1/swagger.json',
    });
    httpMock.expectOne('/swagger/v1/swagger.json').flush(DOC);
    await fixture.whenStable();

    expect(fixture.componentInstance.endpoints()).toHaveLength(1);
  });

  it('falls back to the default OpenAPI path when the config request fails', async () => {
    const fixture = TestBed.createComponent(EndpointFormComponent);
    await fixture.whenStable();
    httpMock.expectOne('api/config').flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    expect(fixture.componentInstance.endpoints()).toHaveLength(1);
  });

  it('filters the endpoint list by route', async () => {
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.setRouteFilter('orders');
    expect(fixture.componentInstance.endpoints()).toHaveLength(1);

    fixture.componentInstance.setRouteFilter('nothing-matches');
    expect(fixture.componentInstance.endpoints()).toHaveLength(0);
  });

  it('dispatches the filled form and emits the returned trace id', async () => {
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    let dispatchedTraceId: string | null = null;
    fixture.componentInstance.traceDispatched.subscribe((traceId) => (dispatchedTraceId = traceId));

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
    fixture.componentInstance.setFieldValue('id', '5');
    fixture.componentInstance.setFieldValue('body', '{"customerName":"Acme"}');
    fixture.componentInstance.send();
    await fixture.whenStable();

    const req = httpMock.expectOne('api/orders/5');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ customerName: 'Acme' });
    req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't1' } });
    await fixture.whenStable();

    expect(fixture.componentInstance.lastTraceId()).toBe('t1');
    expect(dispatchedTraceId).toBe('t1');
  });

  it('sends a configured custom header on the dispatched request', async () => {
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
    fixture.componentInstance.setFieldValue('id', '5');
    fixture.componentInstance.setFieldValue('body', '{"customerName":"Acme"}');
    fixture.componentInstance.addHeader();
    fixture.componentInstance.setHeaderName(0, 'X-Tenant');
    fixture.componentInstance.setHeaderValue(0, 'acme');
    fixture.componentInstance.send();
    await fixture.whenStable();

    const req = httpMock.expectOne('api/orders/5');
    expect(req.request.headers.get('X-Tenant')).toBe('acme');
    req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't1' } });
  });

  it('ignores a header row left with a blank name', async () => {
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
    fixture.componentInstance.setFieldValue('id', '5');
    fixture.componentInstance.setFieldValue('body', '{"customerName":"Acme"}');
    fixture.componentInstance.addHeader();
    fixture.componentInstance.setHeaderValue(0, 'acme');
    fixture.componentInstance.send();
    await fixture.whenStable();

    const req = httpMock.expectOne('api/orders/5');
    expect(req.request.headers.keys()).toEqual([]);
    req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't1' } });
  });

  it('blocks the send and flags the empty field when a path param is left blank', async () => {
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    fixture.componentInstance.selectEndpoint('POST', '/api/orders/{id}');
    fixture.componentInstance.setFieldValue('body', '{"customerName":"Acme"}');
    fixture.componentInstance.send();
    await fixture.whenStable();

    expect(fixture.componentInstance.invalidFieldName()).toBe('id');
    // pt-BR is the default language.
    expect(fixture.nativeElement.textContent).toContain(
      'Preencha o parâmetro de rota "id" antes de enviar.',
    );
    httpMock.expectNone('/api/orders/');
    httpMock.expectNone('api/orders/');
  });

  it('never renders a Scenarios tab or any hardcoded sample route', async () => {
    const fixture = await createComponentWithConfig();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();

    const nativeElement: HTMLElement = fixture.nativeElement;
    expect(nativeElement.querySelector('[role="tab"]')).toBeNull();
    expect(nativeElement.querySelector('[role="tablist"]')).toBeNull();
    expect(nativeElement.textContent).not.toContain('/api/orders/1');
  });
});
