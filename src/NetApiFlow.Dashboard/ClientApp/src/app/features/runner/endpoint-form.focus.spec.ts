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
  hostPathBase: '',
};

const DOC: OpenApiDocument = {
  paths: {
    '/api/orders/{id}': {
      get: {
        operationId: 'GetOrder',
        parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'integer' } }],
      },
    },
    '/api/orders': {
      get: { operationId: 'ListOrders' },
    },
  },
};

const ORDER_BY_ID = 'GET /api/orders/{id}';
const LIST_ORDERS = 'GET /api/orders';

describe('keyboard focus in the endpoint form', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<EndpointFormComponent>;
  let root: HTMLElement;

  beforeEach(async () => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EndpointFormComponent);
    root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    httpMock.expectOne('api/config').flush(CONFIG);
    await fixture.whenStable();
    httpMock.expectOne('/openapi/v1.json').flush(DOC);
    await fixture.whenStable();
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  function endpointButton(key: string): HTMLButtonElement {
    return root.querySelector<HTMLButtonElement>(`[title="${key}"]`)!;
  }

  function control<T extends HTMLElement>(selector: string): T {
    return root.querySelector<T>(selector)!;
  }

  async function pick(key: string): Promise<void> {
    endpointButton(key).focus();
    endpointButton(key).click();
    await fixture.whenStable();
  }

  it('puts the focus on the required path field when an endpoint is picked', async () => {
    await pick(ORDER_BY_ID);

    expect(document.activeElement).toBe(control('#endpoint-form-field-id'));
  });

  it('keeps the path field before "Send request" in the tab order', async () => {
    await pick(ORDER_BY_ID);

    const tabbable = [...root.querySelectorAll<HTMLElement>('button, input, textarea')];
    const indexOf = (element: HTMLElement) => tabbable.indexOf(element);

    expect(indexOf(control('.endpoint-form__back'))).toBeLessThan(
      indexOf(control('#endpoint-form-field-id')),
    );
    expect(indexOf(control('#endpoint-form-field-id'))).toBeLessThan(
      indexOf(control('.endpoint-form__send')),
    );
  });

  it('puts the focus on "Send request" when the endpoint has no field to fill', async () => {
    await pick(LIST_ORDERS);

    expect(document.activeElement).toBe(control('.endpoint-form__send'));
  });

  it('returns the focus to the endpoint that was open when going back to the list', async () => {
    await pick(ORDER_BY_ID);

    control('.endpoint-form__back').focus();
    control<HTMLButtonElement>('.endpoint-form__back').click();
    await fixture.whenStable();

    expect(document.activeElement).toBe(endpointButton(ORDER_BY_ID));
  });

  it('moves the focus to the next header row, then to "Add header", as headers are removed', async () => {
    await pick(ORDER_BY_ID);
    const add = control<HTMLButtonElement>('.endpoint-form__headers-add');
    add.click();
    add.click();
    await fixture.whenStable();
    fixture.componentInstance.setHeaderName(0, 'X-One');
    fixture.componentInstance.setHeaderName(1, 'X-Two');
    await fixture.whenStable();
    const nameInputs = () =>
      [...root.querySelectorAll<HTMLInputElement>('.endpoint-form__header-row input')].filter(
        (_, index) => index % 2 === 0,
      );
    const removeFirstRow = () =>
      root.querySelector<HTMLButtonElement>('.endpoint-form__header-remove')!.click();

    removeFirstRow();
    await fixture.whenStable();
    expect(nameInputs()).toHaveLength(1);
    expect(nameInputs()[0].value).toBe('X-Two');
    expect(document.activeElement).toBe(nameInputs()[0]);

    removeFirstRow();
    await fixture.whenStable();
    expect(document.activeElement).toBe(add);
  });
});
