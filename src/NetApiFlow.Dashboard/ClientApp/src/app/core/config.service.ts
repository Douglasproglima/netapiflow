import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of, shareReplay } from 'rxjs';
import { ApiService } from './api.service';
import { DashboardConfig } from './models';

export const FALLBACK_CONFIG: DashboardConfig = {
  slowThresholdMicros: 250_000,
  version: '',
  tailSamplingEnabled: false,
  successSampleRate: 0,
  openApiDocumentPath: '/openapi/v1.json',
  hostPathBase: '',
};

/**
 * Fetches `GET /config` exactly once for the whole dashboard (M9) and shares the result with
 * every consumer through `shareReplay`, instead of each screen firing its own independent
 * request. Falls back to a reasonable default on failure, so a config outage never blocks the
 * graph or any other consumer from rendering.
 */
@Injectable({ providedIn: 'root' })
export class ConfigService {
  private readonly api = inject(ApiService);

  readonly config$: Observable<DashboardConfig> = this.api.getConfig().pipe(
    catchError(() => of(FALLBACK_CONFIG)),
    shareReplay(1),
  );
}
