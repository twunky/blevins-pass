import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { AppProvider } from './context/AppContext';
import { ThemeProvider } from './theme/ThemeProvider';
import './theme/theme.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppProvider>
      <ThemeProvider>
        <App />
      </ThemeProvider>
    </AppProvider>
  </StrictMode>,
);
