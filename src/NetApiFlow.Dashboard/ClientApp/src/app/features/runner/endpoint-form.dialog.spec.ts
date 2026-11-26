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

describe('the endpoint form as the drawer dialog', () => {
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

  async function useAsDrawer(open: boolean): Promise<void> {
    fixture.componentRef.setInput('asDialog', true);
    fixture.componentRef.setInput('dialogOpen', open);
    await fixture.whenStable();
  }

  function pressEscape(prevented = false): void {
    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    if (prevented) {
      event.preventDefault();
    }
    document.dispatchEvent(event);
  }

  it('is a modal dialog named by its own title while the drawer is open', async () => {
    await useAsDrawer(true);

    const title = root.querySelector('h2')!;
    expect(root.getAttribute('role')).toBe('dialog');
    expect(root.getAttribute('aria-modal')).toBe('true');
    expect(title.textContent!.trim()).not.toBe('');
    expect(document.getElementById(root.getAttribute('aria-labelledby')!)).toBe(title);
  });

  it('is not modal while the drawer is closed, and not a dialog at all on a wide screen', async () => {
    await useAsDrawer(false);
    expect(root.getAttribute('role')).toBe('dialog');
    expect(root.hasAttribute('aria-modal')).toBe(false);

    fixture.componentRef.setInput('asDialog', false);
    await fixture.whenStable();
    expect(root.hasAttribute('role')).toBe(false);
    expect(root.querySelector('.endpoint-form__close')).toBeNull();
  });

  it('puts the focus on the route filter when the drawer opens', async () => {
    await useAsDrawer(true);

    expect(document.activeElement).toBe(root.querySelector('#endpoint-form-filter'));
  });

  it('emits dismissed on Escape while the drawer is open', async () => {
    await useAsDrawer(true);
    const dismissed = vi.fn();
    fixture.componentInstance.dismissed.subscribe(dismissed);

    pressEscape();

    expect(dismissed).toHaveBeenCalledTimes(1);
  });

  it('ignores Escape when the drawer is closed, when it is no drawer, or when something else handled it', async () => {
    const dismissed = vi.fn();
    fixture.componentInstance.dismissed.subscribe(dismissed);

    pressEscape();
    await useAsDrawer(false);
    pressEscape();
    await useAsDrawer(true);
    pressEscape(true);

    expect(dismissed).not.toHaveBeenCalled();
  });

  it('closes with its own button', async () => {
    await useAsDrawer(true);
    const dismissed = vi.fn();
    fixture.componentInstance.dismissed.subscribe(dismissed);

    root.querySelector<HTMLButtonElement>('.endpoint-form__close')!.click();

    expect(dismissed).toHaveBeenCalledTimes(1);
  });

  it('traps the focus only while the drawer is open', async () => {
    const anchors = () => [...root.querySelectorAll<HTMLElement>('.cdk-focus-trap-anchor')];

    await useAsDrawer(true);
    expect(anchors()).toHaveLength(2);
    expect(anchors().every((anchor) => anchor.getAttribute('tabindex') === '0')).toBe(true);

    await useAsDrawer(false);
    expect(anchors().some((anchor) => anchor.hasAttribute('tabindex'))).toBe(false);
  });
});
