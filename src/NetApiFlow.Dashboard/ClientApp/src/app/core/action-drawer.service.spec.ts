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

  describe('the focus around opening and closing', () => {
    let opener: HTMLButtonElement;
    let elsewhere: HTMLButtonElement;

    beforeEach(() => {
      opener = document.body.appendChild(document.createElement('button'));
      elsewhere = document.body.appendChild(document.createElement('button'));
    });

    afterEach(() => {
      opener.remove();
      elsewhere.remove();
    });

    it('gives the focus back to the element that opened the drawer', () => {
      const service = TestBed.inject(ActionDrawerService);
      opener.focus();
      service.toggle();
      elsewhere.focus();

      service.close();

      expect(document.activeElement).toBe(opener);
    });

    it('gives the focus back when the opener toggles the drawer closed', () => {
      const service = TestBed.inject(ActionDrawerService);
      opener.focus();
      service.toggle();
      elsewhere.focus();

      service.toggle();

      expect(document.activeElement).toBe(opener);
    });

    it('moves no focus when the drawer was not open', () => {
      const service = TestBed.inject(ActionDrawerService);
      elsewhere.focus();

      service.close();

      expect(document.activeElement).toBe(elsewhere);
    });

    it('moves no focus when the opener is gone from the page', () => {
      const service = TestBed.inject(ActionDrawerService);
      opener.focus();
      service.toggle();
      elsewhere.focus();
      opener.remove();

      service.close();

      expect(document.activeElement).toBe(elsewhere);
    });
  });
});
