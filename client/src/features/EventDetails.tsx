import { selectedEvent, useApp } from '../context/appState';
import { formatCents, formatLongDate, formatTime } from '../services/format';
import { SeatMap } from './SeatMap';
import { SelectionSummary } from './SelectionSummary';
import { TicketSelector } from './TicketSelector';

export function EventDetails() {
  const { state, dispatch } = useApp();
  const event = selectedEvent(state);

  if (!event) {
    return (
      <section className="card booking-welcome">
        <p className="eyebrow">Something to look forward to</p>
        <h2>Your season.<br /><em>Your kind of escape.</em></h2>
        <p className="muted">Start with an event on the calendar. We’ll help you find a time, choose your tickets, and make it yours.</p>
        <ol className="booking-steps"><li><span>01</span> Pick an experience</li><li><span>02</span> Find your time</li><li><span>03</span> Make a memory</li></ol>
      </section>
    );
  }

  return (
    <section className="card event-details" aria-labelledby="event-heading">
      <div className="event-intro">
        <img src={/winter|snow|concert/i.test(event.name) ? '/images/alpine-luxury.jpg' : '/images/autumn-editorial.jpg'} alt="" className="event-art" width="1536" height="1024" loading="lazy" />
        <div>
          <p className="eyebrow">
            {event.registrationType === 'ReservedSeating' ? 'Reserved seating' : 'General admission'} · {event.venue}
          </p>
          <h2 id="event-heading">{event.name}</h2>
          <p>{event.description}</p>
          {event.fromPriceCents !== null && <p className="muted">From {formatCents(event.fromPriceCents)}</p>}
        </div>
      </div>

      <fieldset className="slot-picker">
        <legend>Choose a time (venue time, Eastern)</legend>
        <div className="slot-options">
          {event.timeSlots.map((slot) => (
            <button
              key={slot.id}
              type="button"
              className={`slot-option${state.selectedSlotId === slot.id ? ' selected' : ''}`}
              aria-pressed={state.selectedSlotId === slot.id}
              disabled={!slot.isBookable}
              onClick={() => dispatch({ type: 'slotSelected', slotId: slot.id })}
            >
              <span>{formatLongDate(slot.startUtc)}</span>
              <strong>
                {formatTime(slot.startUtc)} – {formatTime(slot.endUtc)}
              </strong>
            </button>
          ))}
        </div>
      </fieldset>

      {state.checkout.status === 'error' && state.view === 'browse' && (
        <p className="error" role="alert">
          {state.checkout.error}
        </p>
      )}

      {state.selectedSlotId === null ? (
        <p className="muted">This event has no bookable time slots.</p>
      ) : state.availabilityStatus === 'loading' && !state.availability && !state.seatingAvailability ? (
        <p className="muted">Loading availability…</p>
      ) : state.availabilityStatus === 'error' ? (
        <div className="error" role="alert">
          {state.availabilityError}{' '}
          <button type="button" className="link-button" onClick={() => dispatch({ type: 'availabilityRefreshRequested' })}>
            Retry
          </button>
        </div>
      ) : event.registrationType === 'ReservedSeating' ? (
        <SeatMap />
      ) : (
        <TicketSelector />
      )}

      <SelectionSummary />
    </section>
  );
}
