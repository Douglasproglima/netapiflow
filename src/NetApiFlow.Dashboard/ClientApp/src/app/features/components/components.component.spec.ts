import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ComponentsComponent } from './components.component';
import { ComponentStat } from '../../core/models';

const FIXTURE: ComponentStat[] = [
  {
    component: 'OrderRepository',
    operation: 'FindAsync',
    executionCount: 412,
    errorCount: 3,
    medianDurationMicros: 1_200,
    averageDurationMicros: 1_450,
    p95DurationMicros: 3_100,
    maxDurationMicros: 9_800,
  },
];

describe('ComponentsComponent', () => {
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

  it('renders the exact numbers from the API fixture', async () => {
    const fixture = TestBed.createComponent(ComponentsComponent);
    await fixture.whenStable();
    httpMock.expectOne('api/metrics/components').flush(FIXTURE);
    await fixture.whenStable();

    const row: Element = fixture.nativeElement.querySelector('tbody tr');
    const cells: string[] = Array.from(row.querySelectorAll('td')).map(
      (td) => td.textContent?.trim() ?? '',
    );

    expect(cells[0]).toBe('OrderRepository');
    expect(cells[1]).toBe('FindAsync');
    expect(cells[2]).toBe('412');
    expect(cells[3]).toBe('3');
    expect(cells[4]).toBe('1.2 ms');
    expect(cells[5]).toBe('1.4 ms'); // 1450 µs / 1000, and 1.45 isn't exactly representable in binary
    expect(cells[6]).toBe('3.1 ms');
    expect(cells[7]).toBe('9.8 ms');
  });

  it('shows an explicit empty state when there are no recorded operations', async () => {
    const fixture = TestBed.createComponent(ComponentsComponent);
    await fixture.whenStable();
    httpMock.expectOne('api/metrics/components').flush([]);
    await fixture.whenStable();

    // pt-BR is the default language (D-35).
    expect(fixture.nativeElement.textContent).toContain(
      'Nenhuma métrica de componente registrada ainda.',
    );
  });

  it('shows an explicit error state when the request fails', async () => {
    const fixture = TestBed.createComponent(ComponentsComponent);
    await fixture.whenStable();
    httpMock
      .expectOne('api/metrics/components')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    // pt-BR is the default language (D-35).
    expect(fixture.nativeElement.textContent).toContain(
      'Falha ao carregar as métricas de componentes.',
    );
  });
});
