import { useApp } from '../context/appState';
import type { ThemeName } from '../types/api';

const themes: ThemeName[] = ['Fall', 'Winter'];

export function SiteHeader() {
  const { state, changeTheme, dispatch, resetBooking } = useApp();

  return (
    <header className="site-header">
      <button type="button" className="brand" onClick={() => (state.view === 'confirmation' ? resetBooking() : dispatch({ type: 'reviewClosed' }))}>
        <span className="brand-wordmark">Blevins<span className="brand-mark"> / </span>Pass</span>
        <span className="brand-tagline">Seasonal experiences at Brushfire</span>
      </button>
      <span className="header-note">Good moments. In season.</span>
      <div className="theme-switch" role="group" aria-label="Season theme">
        {themes.map((theme) => (
          <button
            key={theme}
            type="button"
            aria-pressed={state.theme === theme}
            className={state.theme === theme ? 'active' : ''}
            onClick={() => void changeTheme(theme)}
          >
            {theme}
          </button>
        ))}
      </div>
    </header>
  );
}
