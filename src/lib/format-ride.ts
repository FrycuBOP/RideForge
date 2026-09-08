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
