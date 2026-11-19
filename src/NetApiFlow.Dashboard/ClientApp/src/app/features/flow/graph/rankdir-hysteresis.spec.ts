import { nextRankdir } from './rankdir-hysteresis';

describe('nextRankdir', () => {
  it('switches to TB once the ratio reaches 1.2 from LR', () => {
    expect(nextRankdir('LR', 1.2)).toBe('TB');
    expect(nextRankdir('LR', 1.31)).toBe('TB');
  });

  it('switches to LR once the ratio drops to 0.9 from TB', () => {
    expect(nextRankdir('TB', 0.9)).toBe('LR');
    expect(nextRankdir('TB', 0.23)).toBe('LR');
  });

  it('stays LR for a ratio below the TB threshold', () => {
    expect(nextRankdir('LR', 0.89)).toBe('LR');
    expect(nextRankdir('LR', 1.19)).toBe('LR');
  });

  it('stays TB for a ratio above the LR threshold', () => {
    expect(nextRankdir('TB', 0.91)).toBe('TB');
    expect(nextRankdir('TB', 1)).toBe('TB');
  });

  it('keeps the current value inside the dead zone regardless of which side it arrived from', () => {
    // 1.12 sits between the two thresholds: arriving from TB it stays TB, arriving from LR it
    // stays LR (0.9 < 1.12 < 1.2).
    expect(nextRankdir('TB', 1.12)).toBe('TB');
    expect(nextRankdir('LR', 1.12)).toBe('LR');
  });
});
