import { TestBed } from '@angular/core/testing';
import { RunnerCredentialsService } from './runner-credentials.service';

describe('RunnerCredentialsService', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({});
  });

  afterEach(() => {
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  it('reads nothing saved and starts every scheme empty', () => {
    const service = TestBed.inject(RunnerCredentialsService);

    expect(service.authValue('Bearer')).toBe('');
    expect(service.customHeaders()).toEqual([]);
  });

  it('persists an auth value and reads it back from a freshly reconstructed service, simulating a tab refresh', () => {
    let service = TestBed.inject(RunnerCredentialsService);
    service.setAuthValue('Bearer', 'secret-token');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    service = TestBed.inject(RunnerCredentialsService);

    expect(service.authValue('Bearer')).toBe('secret-token');
  });

  it('persists custom headers the same way, surviving a reconstruction', () => {
    let service = TestBed.inject(RunnerCredentialsService);
    service.addHeader();
    service.setHeaderName(0, 'X-Tenant');
    service.setHeaderValue(0, 'acme');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    service = TestBed.inject(RunnerCredentialsService);

    expect(service.customHeaders()).toEqual([{ name: 'X-Tenant', value: 'acme' }]);
  });

  it('keeps two schemes with different names as distinct fields', () => {
    const service = TestBed.inject(RunnerCredentialsService);
    service.setAuthValue('BearerUser', 'token-user');
    service.setAuthValue('BearerAdmin', 'token-admin');

    expect(service.authValue('BearerUser')).toBe('token-user');
    expect(service.authValue('BearerAdmin')).toBe('token-admin');
  });

  it('does not throw when sessionStorage.setItem fails, and keeps the value in memory', () => {
    const service = TestBed.inject(RunnerCredentialsService);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('blocked');
    });

    expect(() => service.setAuthValue('Bearer', 'secret-token')).not.toThrow();
    expect(service.authValue('Bearer')).toBe('secret-token');
  });

  it('does not throw when sessionStorage.getItem fails, and starts empty instead', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('blocked');
    });

    const service = TestBed.inject(RunnerCredentialsService);

    expect(service.authValue('Bearer')).toBe('');
    expect(service.customHeaders()).toEqual([]);
  });

  it('drops a saved value whose scheme is no longer in the reconciled list, keeping the ones that remain', () => {
    const service = TestBed.inject(RunnerCredentialsService);
    service.setAuthValue('Bearer', 'token-a');
    service.setAuthValue('RetiredScheme', 'token-b');

    service.reconcile(['Bearer']);

    expect(service.authValue('Bearer')).toBe('token-a');
    expect(service.authValue('RetiredScheme')).toBe('');
  });

  it('writes the reconciled state through to sessionStorage', () => {
    const service = TestBed.inject(RunnerCredentialsService);
    service.setAuthValue('Bearer', 'token-a');
    service.setAuthValue('RetiredScheme', 'token-b');

    service.reconcile(['Bearer']);

    expect(JSON.parse(sessionStorage.getItem('naf-runner-auth-values') ?? '{}')).toEqual({
      Bearer: 'token-a',
    });
  });
});
