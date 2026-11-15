import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ConfigService, FALLBACK_CONFIG } from './config.service';
import { DashboardConfig } from './models';

const CONFIG: DashboardConfig = {
  slowThresholdMicros: 500_000,
  version: '0.3.1',
  tailSamplingEnabled: true,
  successSampleRate: 0.2,
  openApiDocumentPath: '/openapi/v2.json',
};

describe('ConfigService', () => {
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

  it('fetches GET /config exactly once no matter how many subscribers consume it (M9)', async () => {
    const service = TestBed.inject(ConfigService);

    const first = firstValueFrom(service.config$);
    const second = firstValueFrom(service.config$);

    httpMock.expectOne('api/config').flush(CONFIG);

    expect(await first).toEqual(CONFIG);
    expect(await second).toEqual(CONFIG);
  });

  it('falls back to a reasonable default when GET /config fails, instead of erroring every consumer (M9)', async () => {
    const service = TestBed.inject(ConfigService);

    const result = firstValueFrom(service.config$);

    httpMock.expectOne('api/config').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(await result).toEqual(FALLBACK_CONFIG);
  });
});
