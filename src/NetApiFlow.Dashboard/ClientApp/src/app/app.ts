import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { BreakpointObserver } from '@angular/cdk/layout';
import { filter, map, startWith } from 'rxjs';
import { DASHBOARD_VERSION } from './core/version';
import { AboutDialogComponent } from './features/about/about-dialog.component';
import { ApiService } from './core/api.service';
import { SseService } from './core/sse.service';
import { formatDurationMicros } from './shared/duration-format';

const NARROW_BREAKPOINT = '(max-width: 1023px)';

/**
 * Shell (docs/design/flow-workspace/guide.md, seção 6.1): a 76px icon rail, always visible at
 * 1024px and up (ADR-0010 asks for "icon + label"; stacking them instead of a wide sidebar
 * returns the extra width to the graph, D13), plus a topbar with the current page's title,
 * live-request metadata and the SSE connection chip. Below 1024px the rail becomes an overlay
 * drawer, opened by the topbar hamburger.
 */
@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatSidenavModule, MatButtonModule],
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

  readonly version = DASHBOARD_VERSION;
  readonly connectionState = this.sse.state;

  readonly isNarrow = toSignal(
    this.breakpointObserver.observe(NARROW_BREAKPOINT).pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  readonly sidenavOpened = signal(false);

  readonly pageTitle = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      startWith(null),
      map(() => this.currentRouteData()['title'] as string | undefined),
    ),
    { initialValue: this.currentRouteData()['title'] as string | undefined },
  );

  readonly pageSubtitle = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      startWith(null),
      map(() => this.currentRouteData()['subtitle'] as string | undefined),
    ),
    { initialValue: this.currentRouteData()['subtitle'] as string | undefined },
  );

  readonly slowThresholdLabel = signal<string | null>(null);
  readonly inProgressCount = signal(0);

  ngOnInit(): void {
    this.sse.connect();
    this.api
      .getConfig()
      .subscribe(
        (config) =>
          void this.slowThresholdLabel.set(
            `slow threshold ${formatDurationMicros(config.slowThresholdMicros)}`,
          ),
      );
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
