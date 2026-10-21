import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

function stubPrefersColorScheme(prefersLight: boolean): void {
  vi.stubGlobal(
    'matchMedia',
    vi.fn().mockImplementation((query: string) => ({
      matches: query === '(prefers-color-scheme: light)' && prefersLight,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  );
}

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    delete document.documentElement.dataset['theme'];
    TestBed.configureTestingModule({});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('follows prefers-color-scheme when there is no saved preference', () => {
    stubPrefersColorScheme(true);

    const service = TestBed.inject(ThemeService);

    expect(service.isDark()).toBe(false);
    expect(document.documentElement.dataset['theme']).toBeUndefined();
  });

  it('defaults to dark when the system has no light preference and nothing is saved', () => {
    stubPrefersColorScheme(false);

    const service = TestBed.inject(ThemeService);

    expect(service.isDark()).toBe(true);
  });

  it('reads a previously saved preference and writes data-theme on the document root', () => {
    stubPrefersColorScheme(true);
    localStorage.setItem('naf-theme', 'dark');

    const service = TestBed.inject(ThemeService);

    expect(service.isDark()).toBe(true);
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });

  it('toggle flips the resolved theme and persists it, overriding prefers-color-scheme', () => {
    stubPrefersColorScheme(true); // system prefers light, so isDark() starts false

    const service = TestBed.inject(ThemeService);
    expect(service.isDark()).toBe(false);

    service.toggle();

    expect(service.isDark()).toBe(true);
    expect(document.documentElement.dataset['theme']).toBe('dark');
    expect(localStorage.getItem('naf-theme')).toBe('dark');

    service.toggle();

    expect(service.isDark()).toBe(false);
    expect(document.documentElement.dataset['theme']).toBe('light');
    expect(localStorage.getItem('naf-theme')).toBe('light');
  });
});
