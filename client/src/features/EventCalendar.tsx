import { useMemo, useState } from 'react';
import { selectedEvent, useApp } from '../context/appState';
import { venueDateKey } from '../services/format';
import type { EventSummary } from '../types/api';

const weekdayLabels = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

interface MonthCursor {
  year: number;
  month: number; // 0-11
}

/** Month grid showing every event on each venue-local day it has a time slot. */
export function EventCalendar() {
  const { state, dispatch } = useApp();
  const current = selectedEvent(state);

  const eventsByDay = useMemo(() => groupEventsByDay(state.events), [state.events]);
  const firstEventDay = useMemo(() => [...eventsByDay.keys()].sort()[0], [eventsByDay]);

  // Which month is visible is presentation-only, so it stays local. It opens at the first event month.
  const [cursor, setCursor] = useState<MonthCursor | null>(null);
  const visible = cursor ?? monthOf(firstEventDay);

  const cells = buildMonthCells(visible);
  const monthLabel = new Date(visible.year, visible.month, 1).toLocaleDateString('en-US', { month: 'long', year: 'numeric' });

  return (
    <section className="card calendar" aria-labelledby="calendar-heading">
      <p className="eyebrow calendar-eyebrow">01 / Choose your experience</p>
      <div className="calendar-header">
        <button type="button" className="icon-button" aria-label="Previous month" onClick={() => setCursor(shiftMonth(visible, -1))}>
          ‹
        </button>
        <h2 id="calendar-heading">{monthLabel}</h2>
        <button type="button" className="icon-button" aria-label="Next month" onClick={() => setCursor(shiftMonth(visible, 1))}>
          ›
        </button>
      </div>
      <div className="calendar-grid" role="grid" aria-label={`Events in ${monthLabel}`}>
        {weekdayLabels.map((label) => (
          <div key={label} className="calendar-weekday" role="columnheader">
            {label}
          </div>
        ))}
        {cells.map((cell, index) => {
          const dayEvents = cell ? (eventsByDay.get(cell.key) ?? []) : [];
          return (
            <div key={cell?.key ?? `blank-${index}`} className={`calendar-day${cell ? '' : ' blank'}`} role="gridcell">
              {cell && <span className="day-number">{cell.day}</span>}
              {dayEvents.map((event) => (
                <button
                  key={event.id}
                  type="button"
                  className={`event-chip${current?.id === event.id ? ' selected' : ''}`}
                  aria-pressed={current?.id === event.id}
                  aria-label={`${event.name} on ${cell?.key}`}
                  onClick={() => dispatch({ type: 'eventSelected', eventId: event.id })}
                >
                  <span className="event-chip-name">{event.name}</span>
                  <span className="event-chip-index" aria-hidden="true">{String(state.events.findIndex(item => item.id === event.id) + 1).padStart(2, '0')}</span>
                </button>
              ))}
            </div>
          );
        })}
      </div>
      <p className="calendar-note">Numbered dates match the experiences in the collection above. Select a number to view its times and tickets.</p>
      {state.eventsStatus === 'loading' && <p className="muted">Loading events…</p>}
      {state.eventsStatus === 'error' && <p className="error" role="alert">{state.eventsError}</p>}
      {state.eventsStatus === 'ready' && state.events.length === 0 && <p className="muted">No events are scheduled.</p>}
    </section>
  );
}

function groupEventsByDay(events: EventSummary[]): Map<string, EventSummary[]> {
  const byDay = new Map<string, EventSummary[]>();
  for (const event of events) {
    const days = new Set(event.timeSlots.map((slot) => venueDateKey(slot.startUtc)));
    for (const day of days) {
      byDay.set(day, [...(byDay.get(day) ?? []), event]);
    }
  }
  return byDay;
}

function monthOf(dayKey: string | undefined): MonthCursor {
  if (!dayKey) {
    const today = new Date();
    return { year: today.getFullYear(), month: today.getMonth() };
  }
  const [year, month] = dayKey.split('-').map(Number);
  return { year: year ?? 2026, month: (month ?? 1) - 1 };
}

function shiftMonth({ year, month }: MonthCursor, delta: number): MonthCursor {
  const shifted = new Date(year, month + delta, 1);
  return { year: shifted.getFullYear(), month: shifted.getMonth() };
}

function buildMonthCells({ year, month }: MonthCursor): ({ day: number; key: string } | null)[] {
  const leadingBlanks = new Date(year, month, 1).getDay();
  const daysInMonth = new Date(year, month + 1, 0).getDate();
  const cells: ({ day: number; key: string } | null)[] = Array.from({ length: leadingBlanks }, () => null);
  for (let day = 1; day <= daysInMonth; day++) {
    cells.push({ day, key: `${year}-${String(month + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}` });
  }
  return cells;
}
