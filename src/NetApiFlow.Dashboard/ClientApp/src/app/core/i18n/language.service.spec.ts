import { TestBed } from '@angular/core/testing';
import { LanguageService } from './language.service';

describe('LanguageService', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
  });

  it('defaults to pt-BR when there is no saved preference (D-35, never navigator.language)', () => {
    const service = TestBed.inject(LanguageService);

    expect(service.language()).toBe('pt-BR');
  });

  it('reads a previously saved preference from localStorage', () => {
    localStorage.setItem('naf-lang', 'en');

    const service = TestBed.inject(LanguageService);

    expect(service.language()).toBe('en');
  });

  it('ignores a corrupted/unexpected stored value and falls back to pt-BR', () => {
    localStorage.setItem('naf-lang', 'fr');

    const service = TestBed.inject(LanguageService);

    expect(service.language()).toBe('pt-BR');
  });

  it('setLanguage updates the signal and persists the choice', () => {
    const service = TestBed.inject(LanguageService);

    service.setLanguage('en');

    expect(service.language()).toBe('en');
    expect(localStorage.getItem('naf-lang')).toBe('en');
  });

  it('translate looks up the active dictionary', () => {
    const service = TestBed.inject(LanguageService);

    expect(service.translate('nav.flow')).toBe('Fluxo');

    service.setLanguage('en');

    expect(service.translate('nav.flow')).toBe('Flow');
  });

  it('translate interpolates placeholders without touching the rest of the template', () => {
    const service = TestBed.inject(LanguageService);

    expect(service.translate('topbar.inProgress', { count: 3 })).toBe('3 em andamento');
  });
});
