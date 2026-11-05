import { Hero } from './components/Hero';
import { useEffect, useRef } from 'react';
import { SiteHeader } from './components/SiteHeader';
import { useApp } from './context/appState';
import { EventCalendar } from './features/EventCalendar';
import { EventDetails } from './features/EventDetails';
import { ExperienceCollection } from './features/ExperienceCollection';
import { OrderConfirmation } from './features/OrderConfirmation';
import { ReviewOrder } from './features/ReviewOrder';
import { DemoResetControl } from './features/DemoResetControl';
import { OperatorOutbox } from './features/OperatorOutbox';
import { WaitlistStatusView } from './features/WaitlistStatusView';

export function App() {
  const { state } = useApp();
  const previousView = useRef(state.view);

  // A new checkout screen must start at its heading, not inherit the calendar's scroll position.
  useEffect(() => {
    if (state.view !== 'browse') {
      window.scrollTo({ top: 0, behavior: 'instant' });
    } else if (previousView.current === 'review') {
      document.getElementById('booking')?.scrollIntoView({ block: 'start', behavior: 'instant' });
    }
    previousView.current = state.view;
  }, [state.view]);

  return (
    <>
      <a className="skip-link" href="#main-content">Skip to main content</a>
      <SiteHeader />
      <main className="page" id="main-content">
        {state.view === 'confirmation' ? (
          <OrderConfirmation />
        ) : state.view === 'waitlist' ? (
          <WaitlistStatusView />
        ) : state.view === 'operator' && import.meta.env.DEV ? (
          <OperatorOutbox />
        ) : state.view === 'review' ? (
          <ReviewOrder />
        ) : (
          <>
            <Hero />
            <ExperienceCollection />
            <section className="booking-section" id="booking" aria-labelledby="booking-heading">
              <div className="section-intro booking-intro">
                <div>
                  <p className="eyebrow">Your next good memory</p>
                  <h2 id="booking-heading">A date with the season.</h2>
                </div>
                <p>Select an event, choose a time,<br />and leave the everyday behind.</p>
              </div>
              <div className="booking-layout">
                <EventCalendar />
                <EventDetails />
              </div>
            </section>
          </>
        )}
      </main>
      <footer className="site-footer">
        <div className="footer-wordmark">Blevins / Pass</div>
        <p>A little closer to the season.<br />A little further from ordinary.</p>
        <div className="footer-meta">
          <span>Brushfire · Eastern time</span>
          <span>Demonstration · Stripe test payments only</span>
        </div>
        {import.meta.env.DEV && (
          <>
            <a className="operator-link" href="/?operator">
              Waitlist outbox (development tool)
            </a>
            <DemoResetControl />
          </>
        )}
      </footer>
    </>
  );
}
