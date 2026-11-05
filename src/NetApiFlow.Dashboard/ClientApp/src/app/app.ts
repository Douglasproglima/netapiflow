import { Component, OnDestroy, OnInit, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { BreakpointObserver } from '@angular/cdk/layout';
import { filter, map, startWith } from 'rxjs';
import { AboutDialogComponent } from './features/about/about-dialog.component';
import { ActionDrawerService } from './core/action-drawer.service';
import { ApiService } from './core/api.service';
import { SseService } from './core/sse.service';
import { formatDurationMicros } from './shared/duration-format';
import { LanguageService } from './core/i18n/language.service';
import { TranslatePipe } from './core/i18n/translate.pipe';
import { SupportedLanguage, TranslationKey } from './core/i18n/translations';
import { ThemeService } from './core/theme.service';

const NARROW_BREAKPOINT = '(max-width: 1023px)';

/**
 * Shell: an 84px icon+label rail, plus a topbar with the current page's title, live-request
 * metadata and the SSE connection chip. Below 1024px the rail is an overlay on top of the
 * content, toggled by the topbar hamburger; at 1024px and up it's always docked open and the
 * hamburger is hidden, since there's nothing left to toggle.
 */
@Component({
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatSidenavModule,
    MatButtonModule,
    TranslatePipe,
  ],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly breakpointObserver = inject(BreakpointObserver);
  private readonly dialog = inject(MatDialog);
  private readonly api = inject(ApiService);
  private readonly sse = inject(SseService);
  private readonly language = inject(LanguageService);
  private readonly theme = inject(ThemeService);
  readonly actionDrawer = inject(ActionDrawerService);

  readonly version = signal<string | null>(null);
  readonly connectionState = this.sse.state;
  readonly activeLanguage = this.language.language;
  readonly isDarkTheme = this.theme.isDark;

  readonly isNarrow = toSignal(
    this.breakpointObserver.observe(NARROW_BREAKPOINT).pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  readonly sidenavOpened = signal(true);

  // Route `data.title`/`data.subtitle` are translation keys (app.routes.ts), looked up through
  // the `t` pipe in the template, not literal text (ADR-0011).
  readonly pageTitle = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      startWith(null),
      map(() => this.currentRouteData()['title'] as TranslationKey | undefined),
    ),
    { initialValue: this.currentRouteData()['title'] as TranslationKey | undefined },
  );

  readonly pageSubtitle = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      startWith(null),
      map(() => this.currentRouteData()['subtitle'] as TranslationKey | undefined),
    ),
    { initialValue: this.currentRouteData()['subtitle'] as TranslationKey | undefined },
  );

  readonly slowThresholdValue = signal<string | null>(null);
  readonly inProgressCount = signal(0);

  readonly onFlowRoute = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      startWith(null),
      map(() => this.router.url.split('?')[0] === '/flow'),
    ),
    { initialValue: this.router.url.split('?')[0] === '/flow' },
  );

  constructor() {
    // Open by default on a wide screen, closed by default below 1024px — but only when crossing
    // the breakpoint, so a manual toggle isn't immediately overridden on the next resize tick.
    effect(() => {
      this.sidenavOpened.set(!this.isNarrow());
    });

    // `lang` follows the active language so assistive tech applies the right pronunciation
    // (guide dashboard-preferences, section 7).
    effect(() => {
      document.documentElement.lang = this.activeLanguage();
    });
  }

  ngOnInit(): void {
    this.sse.connect();
    this.api.getConfig().subscribe((config) => {
      this.slowThresholdValue.set(formatDurationMicros(config.slowThresholdMicros));
      this.version.set(config.version);
    });
    this.refreshInProgressCount();
    this.sse.events$.subscribe((event) => {
      if (event.type === 'TraceStarted' || event.type === 'TraceCompleted') {
        this.refreshInProgressCount();
      }
    });
  }

  ngOnDestroy(): void {
    this.sse.disconnect();
  }

  toggleSidenav(): void {
    this.sidenavOpened.update((opened) => !opened);
  }

  closeSidenavIfNarrow(): void {
    if (this.isNarrow()) {
      this.sidenavOpened.set(false);
    }
  }

  openAbout(): void {
    this.dialog.open(AboutDialogComponent, { width: '460px', maxWidth: '90vw' });
  }

  setLanguage(language: SupportedLanguage): void {
    this.language.setLanguage(language);
  }

  toggleTheme(): void {
    this.theme.toggle();
  }

  themeToggleLabel(): TranslationKey {
    return this.isDarkTheme() ? 'theme.toggle.toLight' : 'theme.toggle.toDark';
  }

  private refreshInProgressCount(): void {
    this.api.getLiveTraces().subscribe((traces) => this.inProgressCount.set(traces.length));
  }

  private currentRouteData(): Record<string, unknown> {
    let route = this.router.routerState.root;
    while (route.firstChild) {
      route = route.firstChild;
    }

    return route.snapshot.data;
  }
}
