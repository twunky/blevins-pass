import { useApp } from '../context/appState';
import { formatCents } from '../services/format';

/** A scannable index complements the calendar without duplicating booking state. */
export function ExperienceCollection() {
  const { state, dispatch } = useApp();
  if (state.events.length === 0) return null;

  return (
    <section className="experience-collection" id="experiences" aria-labelledby="collection-heading">
      <div className="section-intro">
        <div><p className="eyebrow">The Brushfire collection</p><h2 id="collection-heading">Make a day of it.</h2></div>
        <p>Choose your kind of escape.<br />Find your moment on the calendar below.</p>
      </div>
      <div className="experience-index">
        {state.events.map((event, index) => (
          <button
            key={event.id}
            type="button"
            className={`experience-link${state.selectedEventId === event.id ? ' selected' : ''}`}
            aria-pressed={state.selectedEventId === event.id}
            onClick={() => {
              dispatch({ type: 'eventSelected', eventId: event.id });
              document.getElementById('booking')?.scrollIntoView({ block: 'start' });
            }}
          >
            <span className="experience-number">{String(index + 1).padStart(2, '0')}</span>
            <span className="experience-name">{event.name}<small>{event.registrationType === 'ReservedSeating' ? 'Live performance · Reserved seats' : 'Seasonal experience · General admission'}</small></span>
            <span className="experience-price">{event.fromPriceCents !== null ? `From ${formatCents(event.fromPriceCents)}` : 'View dates'}<span aria-hidden="true"> ↗</span></span>
          </button>
        ))}
      </div>
    </section>
  );
}
