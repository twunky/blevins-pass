// Display helpers. All event times are shown in venue time (America/New_York).
export const VENUE_TIME_ZONE = 'America/New_York';

const currency = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });

export function formatCents(cents: number): string {
  return currency.format(cents / 100);
}

export function formatTime(isoUtc: string): string {
  return new Date(isoUtc).toLocaleTimeString('en-US', { timeZone: VENUE_TIME_ZONE, hour: 'numeric', minute: '2-digit' });
}

export function formatLongDate(isoUtc: string): string {
  return new Date(isoUtc).toLocaleDateString('en-US', {
    timeZone: VENUE_TIME_ZONE,
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    year: 'numeric',
  });
}

/** "YYYY-MM-DD" of the instant in venue time; used as the calendar's day key. */
export function venueDateKey(isoUtc: string): string {
  return new Date(isoUtc).toLocaleDateString('en-CA', { timeZone: VENUE_TIME_ZONE });
}

/** "EST" or "EDT" for the instant, so customers in other time zones see which clock a time uses. */
export function venueZoneName(isoUtc: string): string {
  const part = new Intl.DateTimeFormat('en-US', { timeZone: VENUE_TIME_ZONE, timeZoneName: 'short' })
    .formatToParts(new Date(isoUtc))
    .find((p) => p.type === 'timeZoneName');
  return part?.value ?? 'ET';
}

/** Full venue date and time with its zone, e.g. "Saturday, November 14, 2026 at 10:00 AM EST". */
export function formatVenueDateTime(isoUtc: string): string {
  return `${formatLongDate(isoUtc)} at ${formatTime(isoUtc)} ${venueZoneName(isoUtc)}`;
}
