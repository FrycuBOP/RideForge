/**
 * Rider-facing formatting for a generated route's stats. Shared by the native map results screen
 * and its web sibling so both read the same numbers the same way.
 */

/** Metres → a compact `"42.3 km"`. Distance is the primary S-01 stat (the rider asked for it). */
export function formatDistanceKm(meters: number): string {
  return `${(meters / 1000).toFixed(1)} km`;
}

/** Seconds → `"45 min"` / `"2 h 15 min"`. Duration is derived display only, never an input. */
export function formatDuration(seconds: number): string {
  const totalMinutes = Math.max(0, Math.round(seconds / 60));
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  return hours === 0 ? `${minutes} min` : `${hours} h ${minutes} min`;
}

/**
 * ISO timestamp → a rider-facing saved date, e.g. `"12 Sep 2026"`.
 *
 * Never throws. This runs once per row of the saved-routes list, so a single unparseable value —
 * or a runtime whose `Intl` data is missing — must degrade to one odd-looking row, not blank the
 * whole list. Both failure modes are caught: a bad date first, then the formatter itself, which
 * falls back to the ISO date portion.
 */
export function formatSavedAt(isoTimestamp: string): string {
  const saved = new Date(isoTimestamp);
  if (Number.isNaN(saved.getTime())) return 'Date unavailable';

  try {
    return saved.toLocaleDateString(undefined, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
  } catch {
    return saved.toISOString().slice(0, 10);
  }
}
