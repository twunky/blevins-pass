# BLEVINS PASS
## Complete Project Handoff, Requirements & Implementation Blueprint

**Document version:** 2.1 — approved workflow amendment  
**Project status:** Final documentation closeout; current results in [FINAL-HANDOFF](docs/FINAL-HANDOFF.md)  
**Implementation deadline:** Original 9–11 p.m. window retained; owner-authorized overtime recorded in [PROJECT-TIMELINE](docs/PROJECT-TIMELINE.md)  
**Project owner:** User  
**Project orchestrator:** ChatGPT / Codex  
**Implementation agent:** Claude Code  
**Git operations:** User only — strictly enforced

## Approved workflow amendment — read before the baseline

**Closeout precedence:** this handoff retains the original specification and historical proposals below. [FINAL-HANDOFF](docs/FINAL-HANDOFF.md) describes actual delivered behavior, verification limits and unconfirmed provider outcomes; [DESIGN_SYSTEM](DESIGN_SYSTEM.md) describes the implemented UI; [PROJECT-TIMELINE](docs/PROJECT-TIMELINE.md) records the late start, environment issues and explicit extension. Do not treat older “not authorized / not started” status passages or the original strict deadline as a claim that later authorized work did not occur. No further application changes are launched by the closeout documentation.

The owner approved one comprehensive Claude Code implementation prompt in place of eight separate implementation approvals. [The approved review and revised plan](project-review-and-revised-plan.md) supplies the explicit architecture, lifecycle, schedule, readability, and contingency amendments to this baseline. [The master implementation prompt](CLAUDE-MASTER-IMPLEMENTATION-PROMPT.md) is the active execution entry point. [AGENTS.md](AGENTS.md) contains shared development boundaries; [CLAUDE.md](CLAUDE.md) is Claude's short entry guide.

This amendment supersedes the per-task approval and prompt-handoff provisions in sections 14 and 16, the original schedule/task sequencing in sections 18 and 19, and the standalone TASK-001 launch instructions in section 23 and the closing status. TASK-001 through TASK-008 remain internal milestones under the revised schedule. Section 15's execution metrics are retained in one consolidated build log with per-milestone timing/evidence rather than separate approval reports. Once the owner explicitly launches the master prompt, Claude may implement, verify, correct, and continue through those milestones without routine approval pauses. Verification is mandatory; automatic progression is not automatic acceptance. Human review follows the build and can occur during it without blocking routine progress.

Claude pauses dependent work for a material unresolved requirement, a necessary out-of-scope change, or missing permission/credentials; it continues useful independent authorized work within the deadline. Ordinary implementation decisions, routine fixes, tests, and documentation updates are preapproved within scope. No AI Git commands, commits, pushes, merges, or history changes are permitted. No application code is authorized by this documentation update, and no task starts automatically at 9 p.m. The planned build window remains November 1, 2026, 9–11 p.m. America/New_York.

The original assignment images and business requirements remain the acceptance baseline. The sections below retain the v2.0 baseline for traceability and must be read with the approved amendments above; superseded workflow passages are historical, not competing execution instructions. The old standalone TASK-001 prompt is retired.

---

# 1. PURPOSE OF THIS HANDOFF

This document is the authoritative starting point for a new ChatGPT/Codex conversation.

The new conversation will have no access to previous discussions. It must use this document to understand:

- The original interview assignment.
- The application's purpose and functionality.
- The technical requirements.
- The architecture and design decisions.
- The Fall and Winter themes.
- General Admission and Reserved Seating workflows.
- The interactive seating chart.
- Stripe integration.
- The AI-assisted development approach.
- Repository documentation and contracts.
- Task execution logging and token tracking.
- The two-hour development strategy.
- The first development task.

Do not assume that any code has been written.

Do not assume that a repository exists or that implementation has started.

The current deliverable is a proposal and implementation blueprint, not an application.

**Do not begin coding until the project owner explicitly authorizes implementation.**

# 2. PROJECT OVERVIEW

## Product name

BLEVINS PASS

## Product description

BLEVINS PASS is a premium seasonal event discovery and ticket registration application.

The platform supports seasonal Fall and Winter attractions, festivals, concerts and special events.

Visitors can discover events, explore available dates and times, select tickets or individual reserved seats, and complete purchases through Stripe Checkout.

Organizers can configure events and select the active seasonal theme.

The application supports two registration experiences:

1. General Admission — customers select time slots and ticket quantities.
2. Reserved Seating — customers select individual seats from an interactive seating chart.

Both registration types use the same application, database, customer checkout process, payment integration and order management foundation.

## Product philosophy

The application should:

- Look like a polished commercial product.
- Have a premium, welcoming seasonal appearance.
- Be interactive and intuitive.
- Demonstrate meaningful business functionality.
- Use a straightforward architecture.
- Be easy for another developer to understand.
- Be friendly to AI-assisted development.
- Satisfy the original assignment within the strict time limit.

We want a distinctive interview demonstration, not an overengineered enterprise application.

The goal is to demonstrate good engineering judgment, not the largest possible feature set.

---

# 3. ORIGINAL INTERVIEW ASSIGNMENT — NON-NEGOTIABLE REQUIREMENTS

The original assignment specifies a strict two-hour development deadline.

These requirements must remain visible throughout planning and implementation.

Do not allow the additional reserved-seating feature, AI documentation or visual enhancements to cause the original requirements to be overlooked.

## 3.1 Event calendar

The application must:

- Display at least five events.
- Display events on an interactive calendar.
- Allow a visitor to select an event.
- Provide at least two time slots for each event.

## 3.2 Ticket selection

After selecting an event and time slot, visitors must be able to:

- View available ticket types.
- See how many tickets remain in each type.
- Select ticket quantities.
- Review the order before purchasing.

Examples of ticket types:

- Adult.
- Child.
- General Admission.
- VIP.

Each time-slot and ticket-type combination has its own capacity.

Example:

| Event | Time | Ticket type | Capacity |
|---|---|---|---:|
| Autumn Festival | 10 AM | Adult | 100 |
| Autumn Festival | 10 AM | Child | 50 |
| Autumn Festival | 2 PM | Adult | 100 |
| Autumn Festival | 2 PM | Child | 50 |

Capacity must be independently enforced for every combination.

## 3.3 Payment

The application must:

- Allow visitors to purchase tickets.
- Use Stripe Checkout or Stripe Elements.
- Use Stripe sandbox/test mode only.
- Use a free Stripe account.
- Prefer Stripe-hosted Checkout.

Webhooks are optional under the original assignment.

The assignment explicitly permits confirming payment when the customer returns from Stripe Checkout.

The backend must verify the Checkout Session with Stripe. Do not trust frontend success messages or return URL parameters alone.

## 3.4 Capacity enforcement

The backend must enforce inventory.

The frontend cannot be trusted to determine whether tickets are available.

The application must prevent customers from purchasing more tickets than the remaining capacity.

## 3.5 Identity

Customer identification is flexible.

For the initial implementation, use email collection during checkout.

Do not spend time building authentication, registration or password management.

## 3.6 Technical requirements

| Area | Required technology |
|---|---|
| Backend | ASP.NET Core Web API |
| Framework | .NET 10 |
| Frontend | React |
| Language | TypeScript |
| Frontend state | React Context API |
| Database | SQLite |
| Payments | Stripe Checkout |
| Testing | Tests proving business rules |

The React Context API must be the single source of frontend application state.

SQLite must use one database file. No separate database server.

## 3.7 Running the application

The application must run locally on a machine that already has .NET 10 and Node.js installed.

Expected startup commands:

Backend:

`dotnet run`

Frontend:

`npm run dev`

The root README must contain exact instructions, including:

- Prerequisites.
- Configuration.
- Database initialization.
- Seed data.
- Stripe test credentials.
- Backend startup.
- Frontend startup.
- Browser URL.
- Any additional required commands.

## 3.8 Creative freedom

The assignment explicitly encourages creative shortcuts.

It does not require a highly available, production-hardened application.

Take sensible shortcuts and document why.

Do not sacrifice important business rules to achieve visual polish.

## 3.9 Submission requirements

The original assignment requires:

1. A private GitHub repository.
2. Reviewer access to the repository.
3. Run instructions in the README.
4. Every AI session transcript.
5. Raw Claude Code session files from `~/.claude/projects/`.
6. A README section explaining unfinished work and what would be done next with more time.

Use Claude Code `/export` for session transcripts.

Include raw session files as required.

For other AI tools, use their available export mechanisms.

Do not expose secrets or credentials in exported documentation.

---

# 4. ADDITIONAL PRODUCT REQUIREMENT — TWO EVENT TYPES

BLEVINS PASS supports both General Admission and Reserved Seating.

These are two booking experiences within one application.

Every event has a registration type that determines which booking interface is displayed.

## 4.1 General Admission

This preserves the original assignment.

Workflow:

1. Browse events.
2. Select an event from the calendar.
3. Select a time slot.
4. View available ticket types.
5. View remaining inventory.
6. Select quantities.
7. Review the order.
8. Proceed to Stripe Checkout.
9. Complete payment.
10. Receive confirmation.

Inventory is quantity-based.

## 4.2 Reserved Seating

This is the additional concert experience.

Workflow:

1. Browse events.
2. Select a concert.
3. Select a performance date and time.
4. View the seating chart.
5. See section names and pricing.
6. Select available individual seats.
7. Review seat selections.
8. Proceed to Stripe Checkout.
9. Complete payment.
10. Receive confirmation showing purchased seats.

Inventory is seat-based.

The same checkout, order and payment infrastructure must support both registration types.

## 4.3 Registration type

Add an event property:

`RegistrationType`

Suggested values:

- GeneralAdmission
- ReservedSeating

Use an enum or another simple, explicit representation.

Do not create separate applications or completely separate event systems.

Do not build a complicated registration-type framework.

---

# 5. RESERVED SEATING — SMALL DEMONSTRATION

## Design decision

Build a custom interactive SVG seating chart.

Keep it intentionally small.

We do not need hundreds or thousands of seats, complicated stadium geometry, multiple levels, accessible seating management systems or advanced seating-map editors.

The purpose is to demonstrate:

- Individual seat selection.
- Different section prices.
- Real availability.
- Sold seats shown in red.
- Prevention of duplicate purchases.
- Integration with Stripe Checkout.

A small, functional seating chart is preferable to a large, visually impressive mockup that does not actually work.

## 5.1 Recommended initial seating layout

Use one seeded concert event.

For example:

**Winter Lights Concert**

Three seating sections:

| Section | Price | Rows | Seats per row | Total |
|---|---:|---:|---:|---:|
| VIP Center | $125 | 3 | 4 | 12 |
| Orchestra Left | $85 | 3 | 4 | 12 |
| Orchestra Right | $85 | 3 | 4 | 12 |
| Total | | | | 36 |

These are demonstration values.

The event should have at least two performance time slots.

The same physical seats can be purchased for either performance.

Each performance maintains independent seat availability.

36 seats are sufficient for the initial demonstration.

If implementation time becomes constrained, reduce the chart to 18–24 seats rather than spending time creating a larger seating map.

## 5.2 Seating chart appearance

The SVG should display:

- Stage.
- Three seating sections.
- Row labels.
- Individually selectable seats.
- Section labels.
- Section pricing.
- Seat availability legend.

Use a simple symmetrical or slightly curved arrangement.

Avoid complex geometry.

Suggested conceptual layout:

```text
              CONCERT STAGE

        ┌───────────────────────┐
        │       VIP CENTER      │
        │   A  ● ● ● ●          │
        │   B  ● ● ● ●          │
        │   C  ● ● ● ●          │
        └───────────────────────┘

 ┌────────────────┐  ┌────────────────┐
 │ ORCHESTRA LEFT │  │ ORCHESTRA RIGHT│
 │ A  ● ● ● ●     │  │ A  ● ● ● ●     │
 │ B  ● ● ● ●     │  │ B  ● ● ● ●     │
 │ C  ● ● ● ●     │  │ C  ● ● ● ●     │
 └────────────────┘  └────────────────┘
```

This is a conceptual arrangement, not a requirement for exact positioning.

## 5.3 Seat appearance and behavior

| Status | Color | Behavior |
|---|---|---|
| Available | Green | Selectable |
| Sold | Red | Disabled |
| Selected | Blue | Click to deselect |
| Temporarily reserved | Amber | Disabled |
| Inactive | Gray | Disabled |

Previously purchased seats must always appear red.

Red seats must never be selectable.

The availability colors must remain consistent in both seasonal themes.

## 5.4 Seat selection

Each seat has:

- Unique identifier.
- Section.
- Row.
- Seat number.
- Price derived from its section.
- Current performance availability.

Example identifiers:

- VIP-CENTER-A-01
- VIP-CENTER-A-02
- ORCHESTRA-LEFT-B-03
- ORCHESTRA-RIGHT-C-04

When a customer selects a seat:

- Change its visual state to blue.
- Display its identifier.
- Display its section.
- Display its price.
- Update the order summary.
- Allow removal from the selection.

A customer should be able to select multiple seats in one order.

The SVG must be responsive and usable on mobile.

Use horizontal scrolling or controlled scaling when necessary.

Do not make seats so small that they are difficult to select.

## 5.5 SVG implementation

Use native React SVG rendering.

A third-party seating-chart service is unnecessary.

Each seat should have a stable identifier and accessible label.

Support:

- Mouse selection.
- Keyboard interaction.
- Visible focus.
- Accessible seat descriptions.
- Disabled states.

The SVG is presentation only.

The database and backend determine seat availability.

Never trust the SVG or frontend state to establish whether a seat is available.

---

# 6. FALL AND WINTER THEMING

The application supports two dynamically selectable seasonal themes.

These are not separate websites.

One shared application uses one active theme.

An organizer can switch between Fall and Winter.

The selected theme is persisted in SQLite and loaded when the application starts.

## 6.1 Fall theme

Personality:

- Warm.
- Welcoming.
- Natural.
- Elegant.
- Harvest-inspired.

Suggested colors:

- Earthy brown.
- Muted burnt orange.
- Copper.
- Amber.
- Warm gold.
- Cream.
- Ivory.

Suggested photography:

- Autumn landscapes.
- Golden foliage.
- Harvest environments.
- Pumpkins.
- Lanterns.
- Woodland trails.
- Warm seasonal lighting.

Avoid cartoonish graphics or excessive orange.

## 6.2 Winter theme

Personality:

- Peaceful.
- Magical.
- Elegant.
- Atmospheric.
- Welcoming.

Suggested colors:

- Deep navy.
- Evergreen.
- Muted blue.
- Antique gold.
- Winter ivory.
- Frosted gray.

Suggested photography:

- Snow-covered forests.
- Winter cabins.
- Illuminated trails.
- Lanterns.
- Snowy landscapes.
- Warm lighting against cool backgrounds.

Avoid an overwhelmingly blue or icy interface.

## 6.3 Shared design language

Both themes share:

- Navigation.
- Page structure.
- Components.
- Layout.
- Typography hierarchy.
- Spacing.
- Calendar.
- Event discovery.
- General Admission booking.
- Reserved Seating.
- Checkout.
- Confirmation.

Only theme configuration, colors, photography and decorative treatments change.

## 6.4 Dynamic behavior

The organizer selects a theme.

The frontend sends the change to the API.

The API persists the setting in SQLite.

The React theme provider updates the application.

Every page reflects the new theme.

Changing themes must never:

- Clear the cart.
- Clear selected tickets.
- Reset the current booking.
- Change ticket prices.
- Change inventory.
- Affect payment processing.

Use CSS custom properties and a small TypeScript theme configuration.

## 6.5 Background and photography direction

An important design decision from our mockup reviews:

Do not use one oversized seasonal background throughout the application.

Instead:

- Use atmospheric photography for the homepage hero.
- Use individual photography for event cards.
- Use clean content surfaces.
- Keep backgrounds subtle.
- Use restrained overlays.
- Avoid excessive gradients.
- Avoid oversaturated photography.
- Maintain readability.

The site should look premium, not like a generic AI-generated template.

## 6.6 Seasonal Theme Designer skill

We agreed to create one reusable AI skill for both themes.

Do not create it during initial project scaffolding.

Create it when frontend theming begins, if time permits.

Proposed location:

`.claude/skills/seasonal-theme-designer/SKILL.md`

Supporting references:

- references/fall-theme.md
- references/winter-theme.md
- references/design-guidelines.md

The skill must understand:

- Fall visual identity.
- Winter visual identity.
- Shared design rules.
- Typography.
- Color palettes.
- Photography.
- Background treatment.
- Responsive layout.
- Accessibility.
- SVG seating-chart presentation.

It must accept the currently selected theme as an input.

It must not duplicate pages or introduce theme-specific business logic.

---

# 7. ARCHITECTURE

## 7.1 Technology stack

| Component | Technology |
|---|---|
| Frontend | React |
| Language | TypeScript |
| Build tool | Vite |
| State | React Context API |
| Backend | ASP.NET Core Web API |
| Framework | .NET 10 |
| Database | SQLite |
| ORM | Entity Framework Core |
| Payments | Stripe Checkout |
| Seating chart | Custom SVG |
| Testing | Focused business-rule tests |

Use straightforward dependencies.

Do not introduce Redux, microservices, message brokers, Kubernetes, complex repositories, unnecessary abstraction layers or a complicated agent framework.

## 7.2 High-level architecture

```text
                 BLEVINS PASS
                       |
              React + TypeScript
                       |
                React Context
                       |
                   API Client
                       |
              ASP.NET Core API
                       |
              Application Services
                       |
                 EF Core
                       |
                    SQLite
                       |
               Stripe Checkout
```

The frontend communicates with the API.

The backend owns business rules, prices, inventory and payment verification.

## 7.3 Frontend structure

Suggested structure:

```text
client/
├── README.md
└── src/
    ├── components/
    │   ├── layout/
    │   ├── calendar/
    │   ├── events/
    │   └── checkout/
    │
    ├── features/
    │   ├── general-admission/
    │   │   ├── TicketSelector.tsx
    │   │   └── Availability.tsx
    │   │
    │   └── reserved-seating/
    │       ├── SeatingChart.tsx
    │       ├── Seat.tsx
    │       ├── SeatLegend.tsx
    │       └── SeatSummary.tsx
    │
    ├── context/
    │   └── AppContext.tsx
    │
    ├── services/
    ├── pages/
    ├── theme/
    │   ├── themes/
    │   │   ├── fall.ts
    │   │   └── winter.ts
    │   ├── ThemeProvider.tsx
    │   └── theme.config.ts
    │
    └── types/
```

This is a proposed organization, not a mandate to create empty folders.

Use React Context API as the single source of frontend application state.

## 7.4 Backend structure

```text
server/
├── README.md
├── EventRegistration.Api/
│   ├── Controllers/
│   ├── Models/
│   ├── DTOs/
│   ├── Data/
│   ├── Services/
│   └── Program.cs
│
└── EventRegistration.Tests/
```

Controllers handle HTTP requests.

Services contain business rules.

EF Core handles persistence.

Do not introduce repository abstractions unless a demonstrated requirement justifies them.

## 7.5 Database

Use one SQLite database file.

No separate database server.

Use EF Core migrations or a simple initialization approach appropriate to the deadline.

Seed the database automatically for the demonstration.

At least five events must be available.

Each event must have at least two time slots.

Include both General Admission and Reserved Seating examples.

---

# 8. DATABASE MODEL

The following is the proposed logical model.

Keep it simple and implement only what is necessary.

## 8.1 Shared entities

### Event

- Id
- Name
- Description
- RegistrationType
- Image
- Active

### TimeSlot

- Id
- EventId
- StartDateTime
- EndDateTime
- Active

### Order

- Id
- CustomerEmail
- TimeSlotId
- Status
- StripeSessionId
- TotalAmount
- CreatedAt

### OrderItem

- Id
- OrderId
- TicketTypeId (nullable for reserved seating)
- SeatReservationId (nullable for general admission)
- Quantity
- UnitPrice

### SiteSettings

- Id
- ActiveTheme

## 8.2 General Admission entities

### TicketType

- Id
- Name
- Description
- Price
- Active

### SlotInventory

- Id
- TimeSlotId
- TicketTypeId
- Capacity
- Reserved
- Sold

Availability:

`Capacity - Reserved - Sold`

Each time-slot and ticket-type combination has independent inventory.

## 8.3 Reserved Seating entities

### SeatingSection

- Id
- EventId
- Name
- Price
- DisplayOrder

### Seat

- Id
- SeatingSectionId
- Row
- SeatNumber
- SvgX
- SvgY
- Active

### SeatReservation

- Id
- TimeSlotId
- SeatId
- OrderId
- Status
- ExpiresAt

A seat belongs to a seating section.

A seat's availability depends on the performance/time slot.

The same seat can be sold for different performances.

A database uniqueness constraint or equivalent transactional protection must prevent the same seat from being reserved or sold twice for the same performance.

The application should not create duplicate physical seats for each performance.

## 8.4 Order behavior

An order supports one registration type per checkout.

General Admission orders contain ticket quantities.

Reserved Seating orders contain individual seat selections.

For the initial implementation, do not support mixed General Admission and Reserved Seating in one order.

This avoids unnecessary complexity.

Prices must be calculated by the backend.

Store money as integer cents, not floating-point currency values.

---

# 9. INVENTORY AND BOOKING BUSINESS RULES

These rules are important and should be documented in:

`docs/contracts/booking-rules.md`

## 9.1 General Admission

- Capacity is independently tracked per time slot and ticket type.
- The backend validates all quantities.
- Quantities must be positive.
- Requested quantities cannot exceed availability.
- Inventory updates must be transactional.
- The frontend cannot determine authoritative availability.

## 9.2 Reserved Seating

Seat states:

- Available.
- Reserved.
- Sold.
- Inactive.

Rules:

1. A customer can select only available seats.
2. Sold seats are red and disabled.
3. Reserved seats are unavailable to other customers.
4. The backend validates every selected seat.
5. All requested seats must be reserved together or none.
6. A seat cannot be purchased twice for the same performance.
7. Section pricing determines the seat price.
8. The frontend cannot submit an authoritative price.
9. Payment success confirms the order and seats.
10. Failed or expired checkout releases the reservation.

## 9.3 Concurrency

Do not rely on the browser to prevent duplicate booking.

Use SQLite transactions, appropriate uniqueness constraints and conditional updates.

A second customer attempting to reserve the same seat must receive an understandable conflict response.

The same applies to General Admission capacity.

## 9.4 Reservation expiration

Temporary reservations should have a short expiration period, such as 10–15 minutes.

Store expiration timestamps in SQLite.

Release expired reservations when inventory is checked or a new reservation is attempted.

Do not implement a complicated background scheduling system.

A simple service or opportunistic cleanup is sufficient for this demonstration.

---

# 10. STRIPE CHECKOUT

Use one shared checkout implementation.

Both registration types must use Stripe Checkout.

## Payment workflow

1. Customer selects tickets or seats.
2. Frontend submits selections.
3. Backend validates availability.
4. Backend calculates prices.
5. Backend reserves inventory.
6. Backend creates a pending order.
7. Backend creates Stripe Checkout Session.
8. Customer is redirected to Stripe.
9. Customer completes test payment.
10. Customer returns to BLEVINS PASS.
11. Backend retrieves and verifies the Stripe Checkout Session.
12. Backend confirms the order.
13. Inventory becomes sold.
14. Confirmation page displays the purchase.

If checkout creation fails or the reservation expires without successful payment, inventory must be released.

Payment confirmation must be idempotent.

Do not trust:

- Frontend success state.
- Query parameters alone.
- Client-submitted totals.
- Client-submitted prices.

## Configuration

Use environment variables or local development configuration.

Example:

```text
Stripe__SecretKey=
Stripe__PublishableKey=
Stripe__WebhookSecret=
```

Only the secret key is required for a basic hosted Checkout integration.

Never expose Stripe secret credentials in React code or commit them.

Use Stripe test mode only.

Webhooks are optional under the original assignment. If time permits, they can improve payment reliability, but they must not displace required functionality.

---

# 11. API CONTRACTS

Proposed endpoints:

| Method | Endpoint | Purpose |
|---|---|---|
| GET | `/api/events` | List events |
| GET | `/api/events/{id}` | Event details |
| GET | `/api/events/{id}/availability` | General Admission availability |
| GET | `/api/events/{id}/seating` | Seating sections and seats |
| GET | `/api/events/{id}/seating/availability?timeSlotId=` | Performance-specific seat availability |
| POST | `/api/orders` | Validate and create pending order |
| GET | `/api/orders/{id}` | Retrieve order |
| POST | `/api/orders/{id}/checkout` | Create Stripe Checkout Session |
| GET | `/api/orders/{id}/confirmation` | Retrieve confirmed order |
| GET | `/api/settings/theme` | Retrieve active theme |
| PUT | `/api/settings/theme` | Update active theme |

Keep the API focused.

Do not build an extensive administrative API.

The organizer only needs a simple way to switch the active theme for this assignment.

For event management, seed data is sufficient.

## Seat availability response

The seating availability endpoint must return availability for the selected performance, not just the event.

For example, Seat A-04 could be sold for the 7 PM performance but available for the 9 PM performance.

## Order creation

The backend should accept either:

- General Admission ticket selections.
- Reserved seat identifiers.

The server determines prices and validates inventory.

Do not allow mixed registration types in one order during the initial implementation.

---

# 12. FRONTEND EXPERIENCE AND PAGES

The application should feel like a real seasonal attraction website.

Suggested pages:

1. Homepage / event discovery.
2. Interactive event calendar.
3. Event details.
4. Time-slot selection.
5. General Admission ticket selection.
6. Reserved Seating chart.
7. Checkout review.
8. Stripe return / confirmation.
9. Organizer theme settings.

All pages must respect the active theme.

## Homepage

- Premium seasonal hero.
- Clear call to action.
- Featured events.
- Upcoming events.
- Event categories or highlights.
- Responsive navigation.

## Calendar

- Display at least five events.
- Allow selection of event dates.
- Show event details when selected.
- Provide at least two time slots for every event.

## Event details

- Event photography.
- Description.
- Date and available times.
- Registration type.
- Clear booking action.

## General Admission

- Ticket types.
- Prices.
- Remaining capacity.
- Quantity selection.
- Order summary.

## Reserved Seating

- Concert details.
- Performance selection.
- Compact SVG seating chart.
- Section pricing.
- Seat availability.
- Selected seats.
- Order summary.

## Checkout review

Display:

- Event.
- Date and time.
- Ticket type or seating section.
- Seat numbers where applicable.
- Quantity.
- Price.
- Total.

## Confirmation

Display:

- Order confirmation.
- Event details.
- Date and time.
- Ticket information.
- Seat assignments where applicable.

## Theme settings

A simple theme selector is sufficient.

Do not build a full administrative portal.

---

# 13. AI-FRIENDLY REPOSITORY

An important project goal is creating a repository that is easy for humans and AI tools to understand.

We want contracts, boundaries, rules, decisions, READMEs and execution logs.

We do not want a complicated AI framework or excessive documentation.

## Proposed repository structure

```text
gather-and-glow/
│
├── README.md
├── AGENTS.md
├── CLAUDE.md
├── .gitignore
│
├── client/
│   └── README.md
│
├── server/
│   └── README.md
│
├── docs/
│   ├── README.md
│   │
│   ├── requirements/
│   │   ├── README.md
│   │   └── functional-requirements.md
│   │
│   ├── architecture/
│   │   ├── README.md
│   │   ├── overview.md
│   │   ├── data-model.md
│   │   └── booking-workflows.md
│   │
│   ├── contracts/
│   │   ├── README.md
│   │   ├── api-contract.md
│   │   ├── booking-rules.md
│   │   └── ai-work-contract.md
│   │
│   ├── decisions/
│   │   └── README.md
│   │
│   ├── tasks/
│   │   └── README.md
│   │
│   └── logs/
│       └── README.md
│
├── ai-sessions/
│   └── README.md
│
└── .claude/
    ├── README.md
    └── skills/
```

Create only files that provide real value.

Do not create empty folders and dozens of placeholder documents.

## 13.1 AGENTS.md

Canonical shared AI instructions.

Include:

- Project overview.
- Requirements.
- Architecture.
- Coding conventions.
- Business rules.
- Testing expectations.
- Documentation expectations.
- AI boundaries.
- Git restrictions.

## 13.2 CLAUDE.md

Claude-specific entry point.

It should reference AGENTS.md rather than duplicate all project instructions.

## 13.3 README files

Each major section should have a concise README explaining:

- Its purpose.
- Its responsibilities.
- Important files.
- Relevant contracts.
- How to work within that section.

## 13.4 Contracts

Create three initial contracts:

1. API contract.
2. Business-rule contract.
3. AI work contract.

Contracts should establish boundaries and expected behavior.

Avoid excessive class-level documentation.

## 13.5 Architecture decisions

Maintain concise architecture decision records.

Suggested decisions:

- ADR-001: Single application architecture.
- ADR-002: React Context API.
- ADR-003: SQLite and EF Core.
- ADR-004: Dynamic Fall/Winter theming.
- ADR-005: Shared checkout and payments.
- ADR-006: General Admission inventory.
- ADR-007: Reserved Seating inventory.
- ADR-008: Custom SVG seating chart.
- ADR-009: Transactional seat reservations.
- ADR-010: Manual Git operations only.

Each decision should document:

- Context.
- Decision.
- Reason.
- Consequences.

Do not write lengthy architectural essays.

---

# 14. AI-ASSISTED DEVELOPMENT WORKFLOW

We are intentionally using two AI tools with separate responsibilities.

## 14.1 ChatGPT / Codex — Project orchestrator

ChatGPT/Codex acts as:

- Senior software architect.
- Technical project manager.
- Requirements analyst.
- Task planner.
- Prompt engineer.
- Code review coordinator.
- Documentation coordinator.

Responsibilities:

1. Maintain project context.
2. Understand original requirements.
3. Track architectural decisions.
4. Break implementation into manageable tasks.
5. Generate comprehensive Claude Code prompts.
6. Review Claude's completion reports.
7. Identify missing requirements.
8. Evaluate test results.
9. Track task status and execution metrics.
10. Decide what should happen next.

ChatGPT/Codex should not blindly generate implementation tasks.

It must evaluate the existing project state before assigning work.

## 14.2 Claude Code — Implementation agent

Claude Code is responsible for implementation.

Responsibilities:

1. Read AGENTS.md and CLAUDE.md.
2. Read relevant section READMEs.
3. Read applicable contracts.
4. Review the existing implementation.
5. Implement only the assigned task.
6. Follow architectural boundaries.
7. Run appropriate tests.
8. Update documentation.
9. Report changed files.
10. Report test results.
11. Report actual execution time.
12. Report token usage when available.
13. Export its AI session transcript.

Claude must not independently expand scope.

## 14.3 User — Project owner

The user:

- Approves requirements and architecture.
- Provides additional design details.
- Reviews implementation.
- Approves completed tasks.
- Provides Claude's responses back to ChatGPT/Codex.
- Manually performs all Git operations.

## 14.4 Strict Git restriction

**AI must NEVER commit or push anything.**

This is an absolute project boundary.

AI must not:

- Commit.
- Push.
- Merge.
- Rebase.
- Cherry-pick.
- Rewrite Git history.

AI can modify application files and documentation within the assigned scope.

The user manually performs every commit and push.

This restriction must appear in AGENTS.md, CLAUDE.md and every implementation prompt.

---

# 15. TASK AND TOKEN LOGGING

We want to understand the actual cost and duration of AI-assisted development.

Each implementation task should have an execution log.

## Required fields

- Task ID.
- Task name.
- Task start timestamp.
- AI execution start timestamp.
- AI execution completion timestamp.
- Task completion timestamp.
- Total elapsed time.
- AI execution duration.
- Human review duration, when available.
- AI tool.
- Model.
- Input tokens.
- Output tokens.
- Cached tokens, when available.
- Other reported usage.
- Files changed.
- Tests executed.
- Results.
- Outstanding issues.

Never invent metrics.

If the tool does not expose a metric, record:

`Not available`

## Example

```markdown
# TASK-003 — Event API

Status: Review

Task Started: 10:15 AM
AI Started: 10:17 AM
AI Completed: 10:26 AM
Task Completed: 10:31 AM

Total Elapsed: 16 minutes
AI Execution: 9 minutes
Human Review: 5 minutes

Tool: Claude Code
Model: Actual model

Input Tokens: Actual usage
Output Tokens: Actual usage
Cached Tokens: Actual usage

Implementation:
- Created event endpoints.
- Added event DTOs.
- Added availability endpoint.

Verification:
- API build successful.
- Focused endpoint tests passed.

Outstanding:
- None identified.

Documentation Updated:
- API contract.
- Server README.
```

These are example fields, not actual execution metrics.

## AI session archives

Retain every AI session.

Suggested structure:

```text
ai-sessions/
├── README.md
├── TASK-001/
│   ├── prompt.md
│   ├── completion-report.md
│   ├── transcript.md
│   └── raw/
│
├── TASK-002/
│   ├── prompt.md
│   ├── completion-report.md
│   ├── transcript.md
│   └── raw/
│
└── TASK-003/
```

For Claude Code:

- Use `/export`.
- Include raw session files from `~/.claude/projects/`.

For ChatGPT/Codex, preserve available session exports.

Never include API keys, passwords or other secrets in transcripts or logs.

---

# 16. DEVELOPMENT WORKFLOW

We want fewer, more complete implementation cycles.

The objective is to avoid giving Claude incomplete prompts, discovering missing requirements and repeatedly sending small corrections.

## Standard task workflow

### Step 1 — Review

ChatGPT/Codex reviews:

- Current project state.
- Original requirements.
- Relevant architecture.
- Previous task reports.
- Outstanding work.

### Step 2 — Prepare prompt

Generate a self-contained implementation prompt.

Every prompt should contain:

- Task ID.
- Objective.
- Context.
- Requirements.
- Files to inspect.
- Files permitted to change.
- Technical constraints.
- Business rules.
- Acceptance criteria.
- Testing expectations.
- Documentation expectations.
- Git restrictions.
- Completion-report requirements.

### Step 3 — Claude implementation

The user provides the prompt to Claude Code.

Claude reads applicable documentation and performs the task.

### Step 4 — Completion report

Claude provides:

- Summary.
- Files changed.
- Implementation details.
- Test commands.
- Actual test results.
- Known limitations.
- Documentation updates.
- Execution duration.
- Token usage when available.

### Step 5 — Review

The user returns Claude's response to ChatGPT/Codex.

ChatGPT/Codex evaluates the work against the task requirements.

### Step 6 — Approval

The user determines whether the work is acceptable.

If corrections are required, prepare one focused correction prompt.

Avoid unnecessary repeated test runs.

### Step 7 — Next task

Only after reviewing the current task should ChatGPT/Codex prepare the next task.

### Step 8 — Manual Git operations

The user manually commits and pushes when appropriate.

---

# 17. TESTING STRATEGY

The assignment specifically requires tests proving business rules.

Testing should be deliberate, not excessive.

Do not run every test after every minor change.

## General Admission tests

At minimum:

1. Valid ticket quantity can be reserved.
2. Quantity cannot exceed capacity.
3. Negative or zero quantities are rejected.
4. Different ticket types have independent capacity.
5. Different time slots have independent capacity.
6. Inventory is updated correctly.

## Reserved Seating tests

At minimum:

1. Available seat can be reserved.
2. Sold seat cannot be reserved.
3. Reserved seat cannot be reserved by another customer.
4. Same seat cannot be sold twice for the same performance.
5. Same physical seat can be purchased for a different performance.
6. Section price is used by the backend.
7. Multiple seat reservations are all-or-nothing.
8. Expired reservations can be released.

## Payment tests

At minimum:

- Successful payment confirms the order.
- Repeated confirmation does not duplicate sales.
- Failed checkout does not leave permanent reservations.
- Unpaid expired orders release inventory.

Do not attempt to create an exhaustive test suite within two hours.

Prioritize the business rules most likely to cause incorrect purchases.

---

# 18. TWO-HOUR IMPLEMENTATION STRATEGY

This is the most important scope-control section.

We have a strict two-hour deadline.

The expanded project is larger than the original assignment.

We must be realistic about what can be implemented and verified in that time.

The original requirements remain the highest priority.

Reserved seating is an agreed product feature, but it must not cause the original assignment to fail.

## 18.1 Preparation before starting the timer

Complete these preparations before the two-hour clock begins:

- Confirm .NET 10 is installed.
- Confirm Node.js is installed.
- Confirm Stripe account exists.
- Confirm Stripe test credentials are available.
- Confirm repository access.
- Confirm development environment.
- Confirm AI tools are available.
- Have the project blueprint ready.

The original assignment explicitly says to set up the Stripe account before starting the clock.

Do not spend implementation time creating accounts or installing basic prerequisites.

## 18.2 Revised task schedule

| Task | Time | Target |
|---|---:|---|
| TASK-001 | 0–10 min | Foundation and essential AI documentation |
| TASK-002 | 10–25 min | SQLite, entities and seed data |
| TASK-003 | 25–40 min | Event, time-slot and inventory APIs |
| TASK-004 | 40–57 min | React, calendar and basic seasonal theming |
| TASK-005 | 57–72 min | General Admission booking and capacity |
| TASK-006 | 72–92 min | Compact reserved seating and SVG |
| TASK-007 | 92–108 min | Shared Stripe Checkout and confirmation |
| TASK-008 | 108–120 min | Focused verification, README and submission |

These are target allocations, not guarantees.

The architect/orchestrator must evaluate progress rather than blindly follow the clock.

## 18.3 Priority hierarchy

### Priority 1 — Original assignment requirements

Must be completed:

- Five or more events.
- Interactive calendar.
- At least two time slots per event.
- Ticket types.
- Remaining capacity.
- Ticket purchasing.
- Stripe test checkout.
- Capacity enforcement.
- Business-rule tests.
- Local startup instructions.
- AI transcripts and submission documentation.

### Priority 2 — Reserved Seating demonstration

Implement the smallest complete version:

- One concert event.
- Two performances.
- Three seating sections.
- Approximately 18–36 seats.
- Different prices per section.
- SVG seating chart.
- Individual seat selection.
- Sold seats displayed red.
- Backend seat availability.
- Transactional reservation.
- Shared Stripe Checkout.

Do not build a seating-map administration editor.

Do not build hundreds of seats.

Do not build advanced stadium geometry.

### Priority 3 — Premium visual experience

Use remaining time for:

- Better imagery.
- Fall and Winter theme refinement.
- Typography.
- Responsive layouts.
- Card treatments.
- Small interactions.
- Visual consistency.

## 18.4 Contingency rules

If implementation falls behind schedule:

1. Do not compromise the original assignment requirements.
2. Preserve working General Admission booking.
3. Preserve real capacity enforcement.
4. Preserve Stripe test payment.
5. Reduce the number of seating-chart seats.
6. Reduce visual polish.
7. Use seeded demonstration data.
8. Document unfinished reserved-seating functionality honestly.

A functional 18-seat chart is better than an unfinished 100-seat chart.

A real payment integration is better than a simulated payment screen.

A working capacity rule is better than a visually impressive but incorrect availability display.

If reserved seating cannot be completed safely, document what remains rather than claiming it works.

---

# 19. DEVELOPMENT TASK DEFINITIONS

## TASK-001 — Project Foundation and AI Documentation

**Target:** 0–10 minutes.

Objective:

Establish the minimum application foundation and AI-friendly documentation.

Deliverables:

- React + TypeScript scaffold.
- .NET 10 Web API scaffold.
- Root README.
- AGENTS.md.
- CLAUDE.md.
- Section READMEs.
- Initial contracts.
- Architecture overview.
- Decision log.
- Task log templates.
- AI session documentation.
- Git restrictions.

Acceptance criteria:

- Both applications build.
- Both applications can start.
- Documentation explains the project.
- AI boundaries are established.
- No Git operations have been performed.

Do not implement the full application.

Do not create the Seasonal Theme Designer skill yet.

## TASK-002 — Database and Seed Data

**Target:** 10–25 minutes.

Deliverables:

- SQLite configuration.
- EF Core entities.
- Database initialization.
- Seed data.
- Five or more events.
- Two or more time slots per event.
- General Admission ticket types.
- Independent inventory capacities.
- One reserved-seating concert.
- Three seating sections.
- 18–36 seats.
- Two performances for the concert.
- Default seasonal theme.

Acceptance criteria:

- Database initializes.
- Seed data is available.
- Both registration types are represented.
- Seat availability is performance-specific.

## TASK-003 — Event and Inventory APIs

**Target:** 25–40 minutes.

Deliverables:

- Event listing.
- Event details.
- Time slots.
- General Admission availability.
- Reserved Seating availability.
- Theme settings retrieval.

Acceptance criteria:

- Five or more events can be retrieved.
- Every event has two or more time slots.
- Ticket availability is returned.
- Seat availability is returned for the selected performance.

## TASK-004 — React Foundation and Theming

**Target:** 40–57 minutes.

Deliverables:

- React application.
- React Context API.
- API client.
- Homepage.
- Event calendar.
- Event cards.
- Event details.
- Fall theme.
- Winter theme.
- Theme switching.
- Basic responsive design.

Create the Seasonal Theme Designer skill at this stage only if the foundational work is complete and time permits.

## TASK-005 — General Admission Booking

**Target:** 57–72 minutes.

Deliverables:

- Ticket selector.
- Quantity selection.
- Remaining capacity.
- Order summary.
- Backend quantity validation.
- Transactional inventory reservation.
- Focused business-rule tests.

Acceptance criteria:

- Visitors can select ticket quantities.
- Capacity cannot be exceeded.
- The backend calculates the total.
- Inventory updates correctly.

## TASK-006 — Reserved Seating

**Target:** 72–92 minutes.

Deliverables:

- Compact SVG seating chart.
- Three seating sections.
- Individual seats.
- Section pricing.
- Seat identifiers.
- Seat availability.
- Selection and deselection.
- Sold seats displayed red and disabled.
- Selected seats displayed blue.
- Seat summary.
- Backend reservation logic.

Acceptance criteria:

- Customers can select multiple seats.
- Selected seats update the order summary.
- Section prices are correct.
- Sold seats cannot be selected.
- Backend prevents duplicate reservations.
- Performance-specific availability works.

The chart should remain small.

Do not add a seating-chart editor or complex visualization library.

## TASK-007 — Stripe Checkout

**Target:** 92–108 minutes.

Deliverables:

- Shared order creation.
- Stripe Checkout Session.
- General Admission checkout.
- Reserved Seating checkout.
- Customer redirect.
- Payment verification.
- Order confirmation.
- Inventory finalization.
- Reservation release on failure or expiration.

Acceptance criteria:

- Both registration types can use Stripe Checkout.
- Prices come from the backend.
- Payment is verified server-side.
- Successful payment confirms the order.
- Inventory cannot be oversold.

## TASK-008 — Final Verification and Submission

**Target:** 108–120 minutes.

Deliverables:

- Focused integration verification.
- Critical business-rule test results.
- Root README.
- Client README.
- Server README.
- Known limitations.
- Unfinished work.
- Next steps.
- AI session exports.
- Raw Claude session files.
- Final submission checklist.

Acceptance criteria:

- Both applications have startup instructions.
- Stripe configuration is documented.
- Known limitations are honest.
- Required AI transcripts are retained.
- No Git commit or push has been performed by AI.

---

# 20. FINAL ACCEPTANCE CHECKLIST

The following checklist should be used by ChatGPT/Codex when reviewing the finished application.

## Original assignment

- [ ] At least five events.
- [ ] Interactive calendar.
- [ ] Two or more time slots per event.
- [ ] Ticket types displayed.
- [ ] Remaining capacity displayed.
- [ ] Ticket quantities selectable.
- [ ] Stripe test checkout works.
- [ ] Backend capacity enforcement works.
- [ ] Business-rule tests exist and pass.
- [ ] SQLite uses one file.
- [ ] .NET 10 API starts with documented instructions.
- [ ] React frontend starts with documented instructions.
- [ ] README contains exact run steps.
- [ ] Private GitHub repository is prepared.
- [ ] Reviewer access can be provided.
- [ ] Every AI session is retained.
- [ ] Claude raw session files are included.
- [ ] Unfinished work is documented.
- [ ] Next steps are documented.

## Additional product functionality

- [ ] General Admission event type.
- [ ] Reserved Seating event type.
- [ ] One seeded concert.
- [ ] At least two concert performances.
- [ ] Multiple seating sections.
- [ ] Different prices per section.
- [ ] Compact interactive SVG chart.
- [ ] Individual seat selection.
- [ ] Seat deselection.
- [ ] Red sold seats.
- [ ] Sold seats cannot be selected.
- [ ] Selected seats shown in blue.
- [ ] Seat pricing shown correctly.
- [ ] Backend prevents double booking.
- [ ] Seat availability is performance-specific.
- [ ] Reserved seating can use Stripe Checkout.

## Themes and design

- [ ] Fall theme.
- [ ] Winter theme.
- [ ] Theme persisted in SQLite.
- [ ] Theme dynamically updates pages.
- [ ] Both themes share the same components.
- [ ] Photography is restrained and appropriate.
- [ ] Backgrounds are not oversaturated.
- [ ] Responsive layout.
- [ ] Seating status colors remain consistent.

## AI engineering

- [ ] AGENTS.md.
- [ ] CLAUDE.md.
- [ ] Section READMEs.
- [ ] API contract.
- [ ] Business-rule contract.
- [ ] AI work contract.
- [ ] Architecture decision log.
- [ ] Task execution logs.
- [ ] Token metrics recorded where available.
- [ ] AI session exports.
- [ ] Git restrictions documented.
- [ ] No AI commits or pushes.

---

# 21. KNOWN LIMITATIONS AND FUTURE ENHANCEMENTS

These are deliberately outside the initial implementation scope.

Potential future work:

- Full event administration.
- Seating-chart editor.
- Multiple seating layouts.
- Larger concert venues.
- Accessible seating management.
- Customer accounts.
- Email confirmations.
- QR-code tickets.
- Ticket scanning.
- Refund processing.
- Cancellation workflows.
- Advanced reporting.
- Promotional codes.
- Tax and fee configuration.
- Advanced inventory reporting.
- Webhook-based payment processing.
- Automated reservation expiration.
- Additional seasonal themes.

Do not implement these unless explicitly authorized after the initial assignment.

---

# 22. FINAL ARCHITECTURE DECISIONS

These are the agreed baseline decisions.

| Area | Decision |
|---|---|
| Product | BLEVINS PASS |
| Product type | Seasonal event registration platform |
| Themes | Fall and Winter |
| Theme selection | Dynamic and persisted |
| Visual direction | Premium |
| Event types | General Admission and Reserved Seating |
| Seating chart | Custom interactive SVG |
| Seating size | Small demonstration, 18–36 seats |
| Sold-seat color | Red |
| Selected-seat color | Blue |
| Available-seat color | Green |
| Reserved-seat color | Amber |
| Frontend | React + TypeScript |
| Frontend build | Vite |
| Frontend state | React Context API |
| Backend | ASP.NET Core Web API |
| Framework | .NET 10 |
| Database | SQLite |
| ORM | EF Core |
| Payments | Stripe-hosted Checkout |
| Inventory | Backend-authoritative |
| Seat reservation | Transactional |
| Architecture | Simple shared application |
| AI orchestrator | ChatGPT / Codex |
| AI implementation | Claude Code |
| Documentation | Human-readable Markdown |
| Logging | Task time and AI usage |
| Git commits | User only |
| Git pushes | User only |
| Deadline | 2 hours |

---

# 23. INSTRUCTIONS FOR THE NEW CHATGPT/CODEX SESSION

You are taking over the BLEVINS PASS project.

You are the senior software architect, technical project manager and AI development orchestrator.

This document is your complete project context.

## Your responsibilities

1. Preserve the original assignment requirements.
2. Understand the two-hour implementation deadline.
3. Maintain the agreed architecture.
4. Support both General Admission and Reserved Seating.
5. Keep the seating chart intentionally small.
6. Preserve the Fall/Winter dynamic theme system.
7. Prioritize working business functionality.
8. Maintain concise, useful documentation.
9. Maintain AI contracts and boundaries.
10. Track implementation time and AI token usage where available.
11. Generate complete Claude Code implementation prompts.
12. Review Claude implementation reports.
13. Keep the task sequence organized.
14. Avoid unnecessary testing loops.
15. Avoid feature creep.
16. Maintain an accurate list of unfinished work.

## Absolute restrictions

- Never commit.
- Never push.
- Never merge.
- Never rebase.
- Never manipulate Git history.
- Never invent test results.
- Never invent token metrics.
- Never claim incomplete functionality works.
- Never expose Stripe secrets.
- Never sacrifice original assignment requirements for optional enhancements.

The user manually performs all Git operations.

## How to work

Do not immediately start generating application code.

First:

1. Read this handoff.
2. Confirm the original requirements.
3. Evaluate the architecture and two-hour schedule.
4. Identify any critical gaps.
5. Keep recommendations focused.
6. Prepare TASK-001 as a comprehensive Claude Code prompt.

Do not introduce additional features.

Do not expand the architecture unnecessarily.

Do not create the Seasonal Theme Designer skill during TASK-001.

Do not start implementation without explicit authorization.

## First requested action

Prepare the TASK-001 implementation prompt.

It should be self-contained and include:

- Project context.
- Repository structure.
- Technology requirements.
- Initial scaffolding.
- Documentation requirements.
- AI contracts.
- Git boundaries.
- Acceptance criteria.
- Build verification.
- Execution logging.
- Completion-report requirements.

The task should establish the foundation, not attempt to implement the entire application.

Wait for the project owner's approval before proceeding.

---

# 24. CLOSING PROJECT DIRECTION

BLEVINS PASS should demonstrate thoughtful software engineering within a limited timeframe.

The application should have enough visual personality to stand out, but the underlying business rules must be real.

The most important demonstration points are:

1. A polished seasonal event discovery experience.
2. Dynamic Fall and Winter themes.
3. A functional General Admission booking workflow.
4. A compact but genuinely interactive reserved-seating chart.
5. Correct inventory and capacity enforcement.
6. A shared Stripe Checkout experience.
7. A clean, understandable .NET and React architecture.
8. An organized AI-assisted engineering workflow.

We are deliberately avoiding unnecessary complexity.

The small seating chart is intentional. The two event types share the same foundation. The themes are configuration-driven. The checkout is shared. The AI workflow is documented without introducing an elaborate agent framework.

**The goal is to deliver a complete, memorable demonstration of sound engineering judgment—not to build an unfinished enterprise ticketing platform in two hours.**

**Project status: Proposal finalized. Awaiting explicit authorization to begin TASK-001.**
