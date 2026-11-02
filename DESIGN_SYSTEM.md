# BLEVINS PASS — seasonal design system

Status: implemented UI-001 design reference, November 1, 2026. This document describes the actual React components and CSS in this checkout. Future UI changes must follow it and update it when the implemented language changes.

## Authority and scope

The owner's Autumn Editorial / Alpine Luxury brief extends the original handoff. It authorizes theme-specific typography, hero composition, materials, and component treatments while preserving the same routes, reusable React components, Context state, booking rules, and backend. This resolves the older guidance that themes change colors/photography only. The latest visual brief supersedes the earlier illustrative palettes; it does not change payment, inventory, performance availability, or fixed seat-status semantics.

Read [the approved plan](project-review-and-revised-plan.md), [architecture](docs/architecture.md), [booking rules](docs/contracts/booking-rules.md), and [API contract](docs/contracts/api-contract.md) alongside this reference. No event editor, authentication, extra pages, or payment changes are part of UI-001.

## Visual identities

### Fall — Autumn Editorial

Warm parchment, burgundy ink, muted olive framing, and lantern-lit woodland imagery. Expressive Georgia headlines use tight tracking, generous scale and italic emphasis. The desktop hero is asymmetric: copy on the left, a larger image on the right with an arched top and a small editorial caption. Fine rules and nearly square corners replace rounded-card repetition. The experience index is a two-column list of numbered editorial entries, not a grid of promotional cards.

### Winter — Alpine Luxury

Midnight/charcoal framing, warm ivory content, silver-gray detail and snowy architectural imagery. Palatino headlines are upright with more measured tracking; body text uses Arial. The hero becomes a wide photographic composition with light text over a restrained dark directional overlay. The lodge windows introduce amber warmth without turning the experience blue. Panels have a subtle 6px radius and extremely light shadow; glass panels, glows and parallax are deliberately absent.

## Semantic color tokens

The source is `client/src/theme/theme.css`, applied through `<html data-theme="fall|winter">`.

| Token | Fall | Winter | Role |
|---|---|---|---|
| --color-bg | #f4eee4 | #f0f0eb | Page canvas |
| --color-surface | #fdfaf3 | #fffef8 | Booking/review surfaces |
| --color-text | #302c25 | #23313a | Main text |
| --color-muted | #6b6357 | #58636a | Supporting copy |
| --color-primary | #793c2e | #233c4a | CTA, selected event/slot, totals |
| --color-primary-text | #fffaf1 | #fffdf5 | Text on primary actions |
| --color-accent | #81592f | #626f77 | Eyebrows, rules and small details |
| --color-border | #d7ccbc | #ccd2d1 | Quiet dividers and controls |
| --color-header | #f4eee4 | #182d38 | Header material |
| --color-header-text | #302c25 | #f6f4ec | Header text |
| --color-chip | #e9e3d3 | #e4ebeb | Secondary/selected surfaces |
| --color-deep | #343e32 | #182d38 | Footer and welcome panel |
| --color-soft | #e8ddcb | #e1e7e6 | Reserved palette primitive; not required for every section |

Semantic feedback stays recognizable: error text #a61b1b on #fdecec with a left rule. Textual messages and labels accompany state color. Border colors are decorative separators, not the sole selection indicator.

Seat tokens are shared in both themes: available #2e9d4f; selected #2563eb; held #e0a325; sold #c62828; inactive #9e9e9e. Do not recolor seats to match a seasonal palette. The existing labels, disabled behavior, keyboard handlers, live selection summary, and visible focus remain mandatory.

Seat numbers use dark #132017 on available/held/inactive fills (calculated contrast 4.85:1, 7.57:1 and 6.29:1 respectively); selected and sold seats retain white numbers. Body, muted and accent text on the page canvas calculate to at least 4.5:1 in both themes. These pair checks are not a full accessibility certification or a substitute for testing image overlays and assistive technology.

## Typography, spacing, and materials

- Fall display: Georgia, Times New Roman, serif. Body: Segoe UI, Arial, sans-serif.
- Winter display: Palatino Linotype, Book Antiqua, Georgia, serif. Body: Arial, Segoe UI, sans-serif. These are system stacks; exact rendering depends on the installed fonts. No remote font download is required.
- Hero heading: fluid 3–5.8rem desktop; 3.3–5rem narrow layouts, with approximately 1.02 line height. Fall uses italic accent text, Winter upright ivory text.
- Section headings: fluid 2–3.2rem. Booking/event headings are smaller so controls retain priority.
- Eyebrows: small uppercase, tracked 0.16em; use for short section identifiers, never paragraphs. Ordinary body copy uses approximately 1.6 line height; hero supporting copy 1.9.
- Page maximum: 1440px, 4vw desktop gutters, 5vw narrow gutters. Spacing rhythm uses .5, .75, 1, 1.5, 2, 3 and 4rem; fluid gaps are used for the hero.
- Panels: 1px semantic border, 1–1.5rem padding, 2px Fall or 6px Winter corners. Do not make all sections cards. Collections and page introductions use open canvas plus rules.
- Shadows: only a nearly imperceptible Winter panel lift. Do not add floating colored shadows, decorative blobs or broad glass effects.

## Shared components and page references

| Component / state | Implemented design and behavior |
|---|---|
| SiteHeader | Wordmark with descriptive tagline, optional centered editorial note, always-visible two-button season selector; theme uses Context and existing persisted setting |
| Hero | Seasonal copy and local photographic asset; anchored Explore CTA scrolls to the experience index; image is atmospheric, with empty alt because the copy carries the information |
| ExperienceCollection | All real API events in a numbered two-column index, one column on mobile; title, registration label and server-derived starting price; selection dispatches the existing eventSelected action and scrolls to booking |
| Booking introduction | Distinct section heading and short instructions; calendar remains present and fully functional |
| EventCalendar | Month navigation, weekday headings, sparse ruled cells, event chips with selected styling and aria-pressed; chips remain at least 44px high. On mobile, numbers match the full names in the experience index; full accessible names remain available, with an explanatory note |
| EventDetails | Wide contextual image, registration/venue eyebrow, event title/description, then available slots and tickets/seats. An unselected welcome panel explains the three booking steps |
| Slot selection | Wrapping buttons, two-line date/time, 44px minimum height; active state has border plus inset outline and semantic surface |
| TicketSelector | Ruled rows, clear name/price/count, quantity stepper; unchanged limits and server-backed availability |
| SeatMap | Existing interactive SVG and status/keyboard logic. Transparent 40-unit hit rectangles at a minimum 660px chart width provide at least 44px touch targets; horizontal scrolling stays inside the map |
| SelectionSummary | Light inset summary, live line totals and full-width primary Review action; no new business-state store |
| ReviewOrder | Narrow 780px surface, step eyebrow, structured order table, explicit Stripe test instructions and existing Pay action; column reduction on small screens preserves item/quantity/subtotal |
| OrderConfirmation | Same narrow surface and typography; existing verified/pending/released/error states, retry and persistent order-reference behavior stay intact |
| Footer | Deep seasonal surface, wordmark/editorial copy, Eastern-time and Stripe-test disclosures; not an extra navigation destination. In the Vite dev server only, a dashed rule separates one quiet outlined "Reset demo availability" button with a "Development tool" tag; it opens an inline explanation with Cancel / "Reset and refund" (44px targets, light-on-deep focus ring, `role=status` result line). Same treatment in Fall and Winter; never in production builds |

Buttons have a 44px minimum target (compact theme controls are 40px on mobile). Primary actions are filled and moderately rectangular; the theme selector alone uses a compact segmented capsule. Links use an underline or a clear arrow, not a fake button without behavior. Focus uses an offset 3px outline, with contrast appropriate to header/light/dark surfaces.

## Responsive standards

- Above 1050px: two-column calendar/details; Fall split hero; Winter wide photographic hero.
- At 1050px and below: booking stacks to preserve calendar readability and seat-chart space. Event image and introduction can sit side by side. The nonessential centered header note is hidden.
- At 700px and below: one-column Fall hero with its image retained, one-column experience index, compact header, stacked event introduction and footer. Winter keeps its immersive photo with stronger contrast and adjusted crop. No mobile menu is needed because the only global action is the always-visible theme control.
- Small review tables hide unit-price columns but retain quantity/subtotal and total. Action buttons wrap. Order references wrap. Seat-map scrolling remains contained.
- Do not hide essential time slots, remaining capacity, quantity controls, seat identifiers or checkout states to make a narrow screenshot look cleaner.

## Motion and interaction

Color/background transitions use 160–240ms; the hero arrow moves 3px on hover. Anchor scrolling is smooth. Theme changes do not reload the document or clear booking state. `prefers-reduced-motion: reduce` removes transitions and smooth scrolling. There are no scroll-reveal dependencies, autoplay, parallax, or animated state that delays access to booking controls.

Review and confirmation start at the top of the page rather than inheriting the calendar's scroll position. Returning from review to change a selection scrolls directly back to booking. These orientation changes are immediate, including for reduced-motion users.

## Assets and performance

Generated with the built-in Image Gen tool for this project, then JPEG-encoded at quality 86 without changing composition:

- `client/public/images/autumn-editorial.jpg`: approximately 449 KB; lantern path, Appalachian foliage and distant timber lodge at sunset.
- `client/public/images/alpine-luxury.jpg`: approximately 398 KB; snow-covered lodge, warm windows, lanterns and evergreen forest at blue hour.

These are illustrative AI-generated brand images, not verified photographs of the actual venue. Hero assets are loaded eagerly with explicit dimensions and high fetch priority. Event-detail images reuse the relevant seasonal image and load lazily. The experience index uses text rather than downloading six duplicate promotional thumbnails. Original SVG assets remain available but no longer supply the hero/event-detail art. Both image-generation prompts are retained in [UI-001](docs/tasks/UI-001.md).

## Verification and maintenance

Actual checks and evidence limits belong in [UI-001](docs/tasks/UI-001.md) and [the UI review](docs/ui-review/README.md). Passing build/lint does not prove visual accessibility. Check both themes, keyboard focus, narrow layouts, slot/quantity/seat selection, review, confirmation/errors and reduced motion after relevant changes. Do not claim full WCAG compliance from screenshots. Preserve the backend business-rule contract and Context state ownership when changing presentation.
