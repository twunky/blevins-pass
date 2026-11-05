// Venue-time regression check for the browser formatting helpers, run under different purchaser
// time zones. Usage (Node 24 strips the TypeScript types):
//   node scripts/check-venue-time.ts Pacific/Kiritimati
// (An argument sets process.env.TZ at runtime, which Node applies immediately; some shells drop a TZ prefix.)
// The same UTC instants must always print the same venue (America/New_York) date, time and zone.
import { formatVenueDateTime, venueDateKey } from '../src/services/format.ts';

const cases: [isoUtc: string, expected: string, venueDay: string][] = [
  ['2026-07-15T14:00:00Z', 'Wednesday, July 15, 2026 at 10:00 AM EDT', '2026-07-15'], // summer
  ['2026-12-15T15:00:00Z', 'Tuesday, December 15, 2026 at 10:00 AM EST', '2026-12-15'], // winter
  ['2026-11-01T15:00:00Z', 'Sunday, November 1, 2026 at 10:00 AM EST', '2026-11-01'], // DST ends
  ['2026-03-08T14:00:00Z', 'Sunday, March 8, 2026 at 10:00 AM EDT', '2026-03-08'], // DST starts
  ['2027-01-01T04:30:00Z', 'Thursday, December 31, 2026 at 11:30 PM EST', '2026-12-31'], // next UTC day
  ['2026-11-14T04:59:00Z', 'Friday, November 13, 2026 at 11:59 PM EST', '2026-11-13'], // venue midnight edge
];

if (process.argv[2]) process.env.TZ = process.argv[2];

const offsetMinutes = -new Date('2026-11-13T12:00:00Z').getTimezoneOffset();
let failures = 0;
for (const [iso, expected, venueDay] of cases) {
  const actual = formatVenueDateTime(iso);
  const day = venueDateKey(iso);
  const ok = actual === expected && day === venueDay;
  if (!ok) failures++;
  console.log(`${ok ? 'PASS' : 'FAIL'} ${iso} -> ${actual} [${day}]${ok ? '' : ` expected ${expected} [${venueDay}]`}`);
}
console.log(`Process TZ=${process.env.TZ ?? '(system)'} offset=${offsetMinutes >= 0 ? '+' : ''}${offsetMinutes}min; ${cases.length - failures}/${cases.length} passed`);
process.exit(failures === 0 ? 0 : 1);
