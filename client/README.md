# Client — React + TypeScript + Vite

Read [the central design system](../DESIGN_SYSTEM.md) before styling or adding UI. Autumn Editorial and Alpine Luxury use shared booking components and state, with different typography, hero composition, imagery and surface treatments. `features/ExperienceCollection.tsx` adds a text-first event index alongside the required calendar.

```powershell
npm install
npm run dev      # http://127.0.0.1:5173 (strict port), proxies /api -> http://127.0.0.1:5050
npm run build    # type-check + production bundle
npm run lint
```

| Path | Responsibility |
|---|---|
| `src/context/appState.ts` | `AppState`, actions and reducer: the **single** shared state (events, selection, availability, checkout, theme) |
| `src/context/AppContext.tsx` | `AppProvider`: owns `useReducer`, loads data, runs checkout and return-from-Stripe verification |
| `src/theme/` | `ThemeProvider` mirrors `state.theme` to `<html data-theme>`; `theme.css` holds the CSS variables per season |
| `src/features/` | Calendar, event details, ticket selector, SVG seat map, selection summary, review, confirmation |
| `src/services/api.ts` | Typed fetch wrapper; surfaces the server's `{ error: { code, message } }` |
| `src/types/api.ts` | API response types |

Rules: prices and counts shown here are display copies; the server re-prices and re-checks everything. Changing event or slot clears selections; changing the theme does not. Only the visible calendar month is local component state. No Stripe key is needed in the browser (hosted Checkout redirect).
