import { TestBed } from '@angular/core/testing';
import { ActionDrawerService } from './action-drawer.service';

describe('ActionDrawerService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({});
  });

  it('starts closed', () => {
    const service = TestBed.inject(ActionDrawerService);

    expect(service.open()).toBe(false);
  });

  it('toggles open state', () => {
    const service = TestBed.inject(ActionDrawerService);

    service.toggle();
    expect(service.open()).toBe(true);

    service.toggle();
    expect(service.open()).toBe(false);
  });

  it('closes regardless of current state', () => {
    const service = TestBed.inject(ActionDrawerService);

    service.toggle();
    service.close();

    expect(service.open()).toBe(false);
  });
});
