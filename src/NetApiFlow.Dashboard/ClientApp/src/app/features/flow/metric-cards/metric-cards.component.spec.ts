import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { MetricCardsComponent } from './metric-cards.component';
import { ComponentStat, MetricsSummary } from '../../../core/models';

const SUMMARY: MetricsSummary = {
  totalTraces: 10,
  errorCount: 3,
  errorRate: 0.3,
  averageDurationMicros: 21_500,
  p95DurationMicros: 88_200,
  maxDurationMicros: 412_000,
};

const COMPONENT_STATS: ComponentStat[] = [
  {
    component: 'OrderRepository',
    operation: 'FindAsync',
    executionCount: 40,
    errorCount: 1,
    medianDurationMicros: 1_000,
    averageDurationMicros: 1_200,
    p95DurationMicros: 2_000,
    maxDurationMicros: 3_000,
  },
  {
    component: 'OrderService',
    operation: 'GetAsync',
    executionCount: 10,
    errorCount: 0,
    medianDurationMicros: 1_500,
    averageDurationMicros: 1_600,
    p95DurationMicros: 2_500,
    maxDurationMicros: 3_500,
  },
];

describe('MetricCardsComponent', () => {
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

  it('renders success/errors derived from errorCount and steps executed summed from component metrics', async () => {
    const fixture = TestBed.createComponent(MetricCardsComponent);
    await fixture.whenStable();
    httpMock.expectOne('api/metrics').flush(SUMMARY);
    httpMock.expectOne('api/metrics/components').flush(COMPONENT_STATS);
    await fixture.whenStable();

    const cards = fixture.componentInstance.cards();
    expect(cards).toEqual([
      { label: 'Total traces', value: '10' },
      { label: 'Success', value: '7' },
      { label: 'Errors', value: '3' },
      { label: 'Avg. latency', value: '21.5 ms' },
      { label: 'Max. latency', value: '412.0 ms' },
      { label: 'Steps executed', value: '50' },
    ]);
  });

  it('shows an explicit error state when GET /metrics fails', async () => {
    const fixture = TestBed.createComponent(MetricCardsComponent);
    await fixture.whenStable();
    httpMock.expectOne('api/metrics').flush('boom', { status: 500, statusText: 'Server Error' });
    httpMock.expectOne('api/metrics/components').flush(COMPONENT_STATS);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Failed to load metrics.');
  });
});
