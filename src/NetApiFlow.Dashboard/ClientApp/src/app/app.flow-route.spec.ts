import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { BreakpointObserver } from '@angular/cdk/layout';
import { BehaviorSubject, Subject } from 'rxjs';
import { App } from './app';
import { ActionDrawerService } from './core/action-drawer.service';
import { ImportedTraceService } from './core/imported-trace.service';
import { SseService } from './core/sse.service';
import { ThemeService } from './core/theme.service';
import { FlowEvent } from './core/models';

@Component({ selector: 'app-stub', template: '' })
class StubPageComponent {}

describe('the shell on the Flow route', () => {
  let httpMock: HttpTestingController;

  async function mountOnFlowRoute() {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([{ path: '', component: StubPageComponent, data: { title: 'nav.flow' } }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: BreakpointObserver,
          useValue: { observe: () => new BehaviorSubject({ matches: true, breakpoints: {} }) },
        },
        {
          provide: SseService,
          useValue: {
            state: signal<'connecting' | 'open' | 'closed'>('open'),
            events$: new Subject<FlowEvent>().asObservable(),
            connect: vi.fn(),
            disconnect: vi.fn(),
          },
        },
        { provide: ThemeService, useValue: { isDark: signal(true), toggle: vi.fn() } },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    TestBed.inject(ActionDrawerService).isDrawer.set(true);
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    httpMock.expectOne('api/config').flush({ slowThresholdMicros: 250_000, version: '0.1.0' });
    httpMock.expectOne('api/traces/live').flush([]);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => {
    httpMock?.verify();
  });

  it('names the button that opens the endpoint drawer, because its text is hidden on a narrow screen', async () => {
    const shell = await mountOnFlowRoute();

    const button = shell.querySelector('.app-topbar__try-endpoint')!;
    expect(button.getAttribute('aria-label')).toBe('Testar um endpoint');
  });

  it('says "Ao vivo" in the connection chip while live traces are shown', async () => {
    const shell = await mountOnFlowRoute();

    const chip = shell.querySelector('.naf-chip')!;
    expect(chip.textContent).toContain('Ao vivo');
    expect(chip.classList.contains('naf-chip--live')).toBe(true);
  });

  it('does not say "Ao vivo" while an imported trace is shown, and says what it is showing', async () => {
    const shell = await mountOnFlowRoute();
    TestBed.inject(ImportedTraceService).active.set(true);
    TestBed.tick();

    const chip = shell.querySelector('.naf-chip')!;
    expect(chip.textContent).not.toContain('Ao vivo');
    expect(chip.textContent).toContain('Trace importado');
    expect(chip.classList.contains('naf-chip--live')).toBe(false);
  });
});
