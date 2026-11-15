import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { routes } from './app.routes';

describe('routes', () => {
  it('falls back to the root route for a URL that matches nothing declared', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes)],
    });
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/this-route-does-not-exist');

    expect(router.url).toBe('/');
  });
});
