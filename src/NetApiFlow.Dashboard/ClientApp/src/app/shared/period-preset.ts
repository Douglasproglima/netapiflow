export type PeriodPreset = '15m' | '1h' | '24h' | '7d' | '30d';

const PRESET_MINUTES: Readonly<Record<PeriodPreset, number>> = {
  '15m': 15,
  '1h': 60,
  '24h': 24 * 60,
  '7d': 7 * 24 * 60,
  '30d': 30 * 24 * 60,
};

/** `from` for a period preset, relative to `now`; the preset never sets `to`. */
export function periodPresetFrom(preset: PeriodPreset, now: Date): string {
  return new Date(now.getTime() - PRESET_MINUTES[preset] * 60_000).toISOString();
}
