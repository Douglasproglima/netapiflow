import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, Routes } from '@angular/router';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { BreakpointObserver } from '@angular/cdk/layout';
import { BehaviorSubject, Subject } from 'rxjs';
import { App } from './app';
import { SseService } from './core/sse.service';
import { FlowEvent } from './core/models';
import { ThemeService } from './core/theme.service';

@Component({ selector: 'app-stub', template: '' })
class StubPageComponent {}

// `data.title`/`data.subtitle` are translation keys (see app.routes.ts), not literal text.
const TEST_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'flow' },
  {
    path: 'flow',
    component: StubPageComponent,
    data: { title: 'nav.flow', subtitle: 'topbar.subtitle.flow' },
  },
  {
    path: 'history',
    component: StubPageComponent,
    data: { title: 'nav.history', subtitle: 'topbar.subtitle.history' },
  },
  { path: 'components', component: StubPageComponent, data: { title: 'nav.components' } },
  { path: 'runner', component: StubPageComponent, data: { title: 'nav.runner' } },
];

function fakeBreakpointObserver(initiallyNarrow: boolean) {
  const subject = new BehaviorSubject({ matches: initiallyNarrow, breakpoints: {} });
  return {
    fake: { observe: () => subject.asObservable() },
    emitNarrow: (matches: boolean) => subject.next({ matches, breakpoints: {} }),
  };
}

function fakeSseService() {
  const events = new Subject<FlowEvent>();
  return {
    state: signal<'connecting' | 'open' | 'closed'>('open'),
    events$: events.asObservable(),
    emit: (event: FlowEvent) => events.next(event),
    connect: vi.fn(),
    disconnect: vi.fn(),
  };
}

function fakeThemeService() {
  return { isDark: signal(true), toggle: vi.fn() };
}

describe('App', () => {
  let httpMock: HttpTestingController;

  function setup(initiallyNarrow: boolean) {
    localStorage.clear(); // isolate LanguageService's default (pt-BR, D-35) across tests
    const { fake, emitNarrow } = fakeBreakpointObserver(initiallyNarrow);
    const sse = fakeSseService();
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(TEST_ROUTES),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BreakpointObserver, useValue: fake },
        { provide: SseService, useValue: sse },
        { provide: ThemeService, useValue: fakeThemeService() },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(App);
    return { fixture, emitNarrow, sse };
  }

  function flushShellRequests() {
    httpMock.expectOne('api/config').flush({ slowThresholdMicros: 250_000, version: '0.1.0' });
    httpMock.expectOne('api/traces/live').flush([]);
  }

  afterEach(() => {
    httpMock?.verify();
  });

  it('should create the app', async () => {
    const { fixture } = setup(false);
    await fixture.whenStable();
    flushShellRequests();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders the current route title and a link to each feature route', async () => {
    const { fixture } = setup(false);
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    flushShellRequests();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    // pt-BR is the default language (D-35), so the title renders translated, not the raw "Flow".
    expect(compiled.querySelector('.app-topbar__title')?.textContent).toContain('Fluxo');
    expect(compiled.querySelector('a[href="/"]')).toBeTruthy();
    expect(compiled.querySelector('a[href="/history"]')).toBeTruthy();
    expect(compiled.querySelector('a[href="/components"]')).toBeTruthy();
    expect(compiled.querySelector('a[href="/runner"]')).toBeTruthy();
  });

  it('docks the rail open by default on a wide viewport, with the hamburger also present', async () => {
    const { fixture } = setup(false);
    await fixture.whenStable();
    flushShellRequests();
    await fixture.whenStable();

    expect(fixture.componentInstance.isNarrow()).toBe(false);
    expect(fixture.componentInstance.sidenavOpened()).toBe(true);
    expect(fixture.nativeElement.querySelector('.app-topbar__hamburger')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.app-rail')).not.toBeNull();
  });

  it('fully hides and reopens the rail via the hamburger on a wide viewport', async () => {
    const { fixture } = setup(false);
    await fixture.whenStable();
    flushShellRequests();
    await fixture.whenStable();

    expect(fixture.componentInstance.sidenavOpened()).toBe(true);
    fixture.componentInstance.toggleSidenav();
    expect(fixture.componentInstance.sidenavOpened()).toBe(false);
    fixture.componentInstance.toggleSidenav();
    expect(fixture.componentInstance.sidenavOpened()).toBe(true);
  });

  it('switches to an overlay rail, closed by default, below the breakpoint', async () => {
    const { fixture, emitNarrow } = setup(false);
    await fixture.whenStable();
    flushShellRequests();
    await fixture.whenStable();

    emitNarrow(true);
    await fixture.whenStable();

    expect(fixture.componentInstance.isNarrow()).toBe(true);
    expect(fixture.componentInstance.sidenavOpened()).toBe(false);
    expect(fixture.nativeElement.querySelector('.app-topbar__hamburger')).not.toBeNull();
  });

  it('toggles the overlay rail via the hamburger button below the breakpoint', async () => {
    const { fixture } = setup(true);
    await fixture.whenStable();
    flushShellRequests();
    await fixture.whenStable();

    expect(fixture.componentInstance.sidenavOpened()).toBe(false);
    fixture.componentInstance.toggleSidenav();
    expect(fixture.componentInstance.sidenavOpened()).toBe(true);
    fixture.componentInstance.toggleSidenav();
    expect(fixture.componentInstance.sidenavOpened()).toBe(false);
  });

  it('defaults to pt-BR and re-translates the shell when the language toggle is used', async () => {
    const { fixture } = setup(false);
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    flushShellRequests();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance.activeLanguage()).toBe('pt-BR');
    expect(compiled.querySelector('.app-topbar__title')?.textContent).toContain('Fluxo');

    fixture.componentInstance.setLanguage('en');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.activeLanguage()).toBe('en');
    expect(compiled.querySelector('.app-topbar__title')?.textContent).toContain('Flow');
  });

  it('toggles the theme via the injected ThemeService', async () => {
    const { fixture } = setup(false);
    await fixture.whenStable();
    flushShellRequests();
    await fixture.whenStable();

    fixture.componentInstance.toggleTheme();

    expect(TestBed.inject(ThemeService).toggle).toHaveBeenCalled();
  });

  it('reads the rail version from GET /config, not a build-time constant (D-44)', async () => {
    const { fixture } = setup(false);
    await fixture.whenStable();
    flushShellRequests();
    await fixture.whenStable();

    expect(fixture.componentInstance.version()).toBe('0.1.0');
    expect(fixture.nativeElement.querySelector('.app-rail__logo')?.textContent).toContain('v0.1.0');
  });

  it('shows the in-progress count fetched from GET /traces/live', async () => {
    const { fixture } = setup(false);
    await fixture.whenStable();
    httpMock.expectOne('api/config').flush({ slowThresholdMicros: 250_000, version: '0.1.0' });
    httpMock.expectOne('api/traces/live').flush([{ traceId: 't1' } as never]);
    await fixture.whenStable();

    expect(fixture.componentInstance.inProgressCount()).toBe(1);
  });

  it('coalesces a burst of trace events into at most one extra GET /traces/live call (A2)', async () => {
    const { fixture, sse } = setup(false);
    await fixture.whenStable();
    httpMock.expectOne('api/config').flush({ slowThresholdMicros: 250_000, version: '0.1.0' });
    httpMock.expectOne('api/traces/live').flush([]);
    await fixture.whenStable();

    vi.useFakeTimers({
      toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'],
    });
    for (let i = 0; i < 10; i++) {
      sse.emit({
        eventId: i,
        type: 'TraceCompleted',
        traceId: `t${i}`,
        timestamp: '2026-01-01T00:00:00Z',
      });
    }

    vi.advanceTimersByTime(1500);
    vi.useRealTimers();
    await Promise.resolve();
    fixture.detectChanges();

    const liveRequests = httpMock.match('api/traces/live');
    expect(liveRequests.length).toBeLessThanOrEqual(1);
    liveRequests.forEach((req) => req.flush([{ traceId: 't9' } as never]));
  });
});
