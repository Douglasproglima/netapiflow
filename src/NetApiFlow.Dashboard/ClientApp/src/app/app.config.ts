import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { demoFixtureInterceptor } from './core/demo-fixture.interceptor';
import { environment } from '../environments/environment';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // Spread, not a ternary returning an array: provideHttpClient takes features as varargs.
    provideHttpClient(
      ...(environment.isDemoMode ? [withInterceptors([demoFixtureInterceptor])] : []),
    ),
    provideRouter(routes),
  ],
};
