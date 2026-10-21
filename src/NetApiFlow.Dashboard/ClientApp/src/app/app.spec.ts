import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BreakpointObserver } from '@angular/cdk/layout';
import { BehaviorSubject } from 'rxjs';
import { App } from './app';
import { routes } from './app.routes';

function fakeBreakpointObserver(initiallyNarrow: boolean) {
  const subject = new BehaviorSubject({ matches: initiallyNarrow, breakpoints: {} });
  return {
    fake: { observe: () => subject.asObservable() },
    emitNarrow: (matches: boolean) => subject.next({ matches, breakpoints: {} }),
  };
}

describe('App', () => {
  it('should create the app', async () => {
    const { fake } = fakeBreakpointObserver(false);
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), { provide: BreakpointObserver, useValue: fake }],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the title and a link to each feature route', async () => {
    const { fake } = fakeBreakpointObserver(false);
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), { provide: BreakpointObserver, useValue: fake }],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.app-shell__topbar-title')?.textContent).toContain('NetApiFlow');
    expect(compiled.querySelector('a[href="/flow"]')).toBeTruthy();
    expect(compiled.querySelector('a[href="/history"]')).toBeTruthy();
    expect(compiled.querySelector('a[href="/components"]')).toBeTruthy();
    expect(compiled.querySelector('a[href="/runner"]')).toBeTruthy();
  });

  it('docks the sidebar open on a wide viewport, with the topbar always rendered', async () => {
    const { fake } = fakeBreakpointObserver(false);
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), { provide: BreakpointObserver, useValue: fake }],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(fixture.componentInstance.isNarrow()).toBe(false);
    expect(fixture.componentInstance.sidenavOpened()).toBe(true);
    expect(fixture.nativeElement.querySelector('.app-shell__topbar')).not.toBeNull();
  });

  it('switches to an overlay sidebar, closed by default, below the breakpoint', async () => {
    const { fake, emitNarrow } = fakeBreakpointObserver(false);
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), { provide: BreakpointObserver, useValue: fake }],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    emitNarrow(true);
    await fixture.whenStable();

    expect(fixture.componentInstance.isNarrow()).toBe(true);
    expect(fixture.componentInstance.sidenavOpened()).toBe(false);
    expect(fixture.nativeElement.querySelector('.app-shell__topbar')).not.toBeNull();
  });

  it('toggles the sidebar open/closed via the hamburger button on a wide viewport', async () => {
    const { fake } = fakeBreakpointObserver(false);
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), { provide: BreakpointObserver, useValue: fake }],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(fixture.componentInstance.sidenavOpened()).toBe(true);

    fixture.componentInstance.toggleSidenav();
    await fixture.whenStable();
    expect(fixture.componentInstance.sidenavOpened()).toBe(false);

    fixture.componentInstance.toggleSidenav();
    await fixture.whenStable();
    expect(fixture.componentInstance.sidenavOpened()).toBe(true);
  });
});
