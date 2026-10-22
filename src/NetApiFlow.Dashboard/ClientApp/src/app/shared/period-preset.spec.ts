import { periodPresetFrom } from './period-preset';

describe('periodPresetFrom', () => {
  const now = new Date('2026-10-21T17:30:00.000Z');

  it('subtracts 15 minutes', () => {
    expect(periodPresetFrom('15m', now)).toBe('2026-10-21T17:15:00.000Z');
  });

  it('subtracts 1 hour', () => {
    expect(periodPresetFrom('1h', now)).toBe('2026-10-21T16:30:00.000Z');
  });

  it('subtracts 24 hours', () => {
    expect(periodPresetFrom('24h', now)).toBe('2026-10-20T17:30:00.000Z');
  });

  it('subtracts 7 days', () => {
    expect(periodPresetFrom('7d', now)).toBe('2026-10-14T17:30:00.000Z');
  });
});
