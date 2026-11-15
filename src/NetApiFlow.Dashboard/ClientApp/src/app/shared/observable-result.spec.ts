import { Subject, switchMap } from 'rxjs';
import { catchAsResult } from './observable-result';

describe('catchAsResult', () => {
  it('wraps a successful emission as { ok: true, value }', () => {
    const results: unknown[] = [];
    const source = new Subject<number>();

    catchAsResult(source).subscribe((result) => results.push(result));
    source.next(42);

    expect(results).toEqual([{ ok: true, value: 42 }]);
  });

  it('wraps an error as { ok: false } instead of letting it reach the subscriber as an error', () => {
    const results: unknown[] = [];
    let errored = false;
    const source = new Subject<number>();

    catchAsResult(source).subscribe({
      next: (result) => results.push(result),
      error: () => (errored = true),
    });
    source.error(new Error('boom'));

    expect(results).toEqual([{ ok: false }]);
    expect(errored).toBe(false);
  });

  it('keeps a switchMap pipeline alive for later emissions after an earlier one errored', () => {
    const trigger = new Subject<void>();
    const results: unknown[] = [];
    let callCount = 0;

    trigger
      .pipe(
        switchMap(() => {
          callCount++;
          const inner = new Subject<string>();
          if (callCount === 1) {
            queueMicrotask(() => inner.error(new Error('first call fails')));
          } else {
            queueMicrotask(() => inner.next('second call succeeds'));
          }
          return catchAsResult(inner);
        }),
      )
      .subscribe((result) => results.push(result));

    trigger.next();
    return Promise.resolve()
      .then(() => Promise.resolve())
      .then(() => {
        trigger.next();
        return Promise.resolve().then(() => Promise.resolve());
      })
      .then(() => {
        expect(results).toEqual([{ ok: false }, { ok: true, value: 'second call succeeds' }]);
      });
  });
});
