import { useEffect, type ReactNode } from 'react';
import { useApp } from '../context/appState';

/**
 * Applies the active theme from AppContext as a data attribute; CSS variables in theme.css do
 * the rest. There is no separate theme store, so switching never touches booking state.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const { state } = useApp();
  useEffect(() => {
    document.documentElement.dataset.theme = state.theme.toLowerCase();
  }, [state.theme]);
  return <>{children}</>;
}
