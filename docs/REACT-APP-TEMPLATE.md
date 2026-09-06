# React Frontend — template organizacije koda

> Samostalan dokument. Kopira se u novi repo kao `CLAUDE.md` ili `docs/ARCHITECTURE.md`.
> Odnosi se na **jednu React SPA aplikaciju** (Vite + TS), ne na monorepo.

---

## 0. Kako se koristi ovaj fajl

1. Kopiraj ga u koren novog projekta.
2. Prođi kroz §2 (stablo) i napravi foldere — prazan folder se ne pravi unapred.
3. Prekopiraj §14 (lint enforcement) u `eslint.config.js` — **pravilo koje se ne proverava
   mašinski biće prekršeno**.
4. Sve dalje odluke se svode na jednu tabelu: §3 „šta gde ide".

Princip iz koga sve ostalo sledi: **kod se seče po domenu, ne po tipu fajla.** Nema foldera
`containers/`, `views/`, `widgets/`, `helpers/` u koje se sleže sve što ne zna gde bi.

---

## 1. Stack (referentni)

| Sloj | Izbor |
|---|---|
| Build | Vite + TypeScript (strict) |
| UI | React 19 (React Compiler ON) |
| Router | React Router (`createBrowserRouter`, objektne rute) |
| Server state | RTK Query |
| Client state | Redux Toolkit (`createSlice`) |
| Forme | React Hook Form + zod |
| Stil | Tailwind + `cva` + `cn()` |
| i18n | i18next |
| Test | Vitest + Testing Library + MSW; Playwright za e2e |

Zameni šta hoćeš — **pravila organizacije ispod ne zavise od biblioteka**, osim tamo gde je
eksplicitno napisano ime alata.

---

## 2. Stablo foldera

```
.
├── src/
│   ├── main.tsx                  # entry — createRoot, ništa drugo
│   ├── App.tsx                   # root kompozicija providera + RouterProvider
│   │
│   ├── providers/                # StoreProvider, I18nProvider, ThemeProvider,
│   │                             # ErrorBoundary, ModalRoot
│   ├── routes/                   # router.tsx, guards/ (RequireAuth, RequireRole)
│   ├── store/                    # store.ts, rootReducer.ts, hooks.ts (useAppDispatch…)
│   │
│   ├── pages/                    # route-level komponente — SAMO kompozicija, BEZ logike
│   │
│   ├── features/                 # domenski moduli — ovde živi 80% koda
│   │   ├── auth/
│   │   ├── projects/
│   │   └── …
│   │
│   ├── components/               # deljene komponente (koristi ih 2+ feature-a)
│   │   ├── ui/                   # primitivi (shadcn output) — FLAT: button.tsx, dialog.tsx
│   │   ├── atoms/                # Icon/, Text/, Spinner/
│   │   ├── molecules/            # FormField/, SearchInput/, Pagination/
│   │   ├── organisms/            # DataTable/, FilterPanel/, ModalShell/
│   │   └── layouts/              # MainLayout/, AuthLayout/
│   │
│   ├── hooks/                    # deljeni hookovi bez domena (useDebounce, useMediaQuery)
│   ├── lib/                      # helperi, config, konstante — ime po poslu, nikad utils.ts
│   ├── locales/                  # common.json, errors.json — globalni namespace-ovi
│   ├── styles/                   # globals.css, tokens.css
│   └── types/                    # samo globalni/ambient tipovi (env.d.ts, vite-env.d.ts)
│
├── e2e/                          # Playwright *.spec.ts
├── public/
├── index.html
├── vite.config.ts
├── eslint.config.js
├── tsconfig.json
└── ARCHITECTURE.md               # ovaj fajl
```

**Prazan folder se ne pravi unapred.** `features/` počinje sa jednim feature-om.

---

## 3. Šta gde ide — tabela odlučivanja

| Pišeš… | Ide u |
|---|---|
| komponentu koju koristi **jedan** feature | `features/<x>/components/` |
| komponentu koju koriste **dva+** feature-a | `src/components/` (atoms/molecules/organisms) |
| primitiv iz shadcn-a | `src/components/ui/` — flat, kako CLI generiše |
| layout rute | `src/components/layouts/` |
| hook koji **zna za domen** | `features/<x>/hooks/` |
| hook koji zna za app ali ne za domen | `src/hooks/` |
| `useAppDispatch` / `useAppSelector` | `src/store/hooks.ts` |
| čistu funkciju bez React-a | `src/lib/<imePoPoslu>.ts` |
| funkciju koju koristi samo jedan feature | `features/<x>/lib/` |
| route-level komponentu | `src/pages/` — **samo kompozicija** |
| zod šemu | `features/<x>/schemas/` |
| RTKQ endpoint | `features/<x>/api/` |
| Redux slice | `features/<x>/store/` |
| prevod feature-a | `features/<x>/locales/{sr,en}.json` |
| globalni prevod | `src/locales/common.json` |
| tip | uz svoj domen: `features/<x>/types.ts` — nikad `types/index.ts` sa 300 linija |

**Pravilo praga:** kod ide na najniži nivo koji ga može držati. Feature pre `components/`,
`components/` pre izdvajanja u paket. Izdiže se **tek kad postoji drugi potrošač**, ne „za
svaki slučaj".

---

## 4. Slojevi i smer importa

Import sme **samo naniže**:

```
providers / routes / store   →  sve
pages                        →  features, components, hooks, lib
features                     →  components, hooks, lib
                                ❌ feature NE SME importovati drugi feature (osim kroz barrel)
components / hooks / lib     →  samo jedni druge i eksterne pakete
lib                          →  ništa iz src/ (zero-dep, čiste funkcije)
```

### Feature ne importuje feature

Ako feature A treba nešto iz B, postoje tačno tri izlaza — i nijedan nije direktan import
unutrašnjeg fajla:

| Situacija | Rešenje |
|---|---|
| deljena **komponenta** | izdigni u `src/components/` |
| deljena **logika** | izdigni u `src/hooks/` ili `src/lib/` |
| treba **podatak** iz drugog domena | čitaj kroz javni hook (`useAuth()`) ili RTKQ `providesTags` |

```ts
// ✅ dozvoljeno — javni API feature-a
import { useAuth } from '@/features/auth';

// ❌ zabranjeno — zaobilazi granicu, lint pada
import { authSlice } from '@/features/auth/store/auth.slice';
```

**Test granice:** feature se obriše sa `rm -rf` i ništa osim njegovih ruta ne pukne.
Ako pukne — granica je propuštena.

---

## 5. Anatomija feature-a

```
features/auth/
├── api/          authApi.ts              — RTKQ injectEndpoints
├── components/   LoginForm/              — komponente ovog domena
├── modals/       LoginModal.tsx
├── hooks/        useAuth.ts, useLogin.ts ← javni API feature-a
├── store/        auth.slice.ts, auth.selectors.ts
├── schemas/      login.schema.ts         — zod
├── lib/          formatSessionExpiry.ts
├── locales/      sr.json, en.json        — namespace "auth"
├── types.ts
├── __tests__/    auth.integration.test.tsx
└── index.ts      — PUBLIC API
```

### Public API je uzak

`index.ts` eksportuje **samo hookove, tipove i komponente**. Slice, selektori i endpointi
ostaju unutra — oni su implementacija.

```ts
// features/auth/index.ts
export { useAuth, useLogin, useLogout } from './hooks';
export { LoginForm } from './components/LoginForm';
export type { AuthUser } from './types';

// ❌ export { authSlice }         — NIKAD
// ❌ export { selectCurrentUser } — NIKAD
```

Razlog: čim slice iscuri napolje, neko će ga dispatch-ovati spolja i granica prestaje da
postoji. Selektor koji stvarno treba drugima → pretvori ga u hook i izdigni.

---

## 6. Komponenta = folder

Van `components/ui/` (shadcn, flat), **svaka komponenta je folder**:

```
ProjectCard/
├── ProjectCard.tsx            # struktura — JSX, bez Tailwind class stringova
├── ProjectCard.variants.ts    # stil — cva
├── ProjectCard.constants.ts   # konstante (opciono)
├── ProjectCard.test.tsx       # test — kolokovan
└── index.ts                   # barrel SAMO ove komponente
```

```ts
// ProjectCard/index.ts
export { ProjectCard } from './ProjectCard';
export type { ProjectCardProps } from './ProjectCard';
```

```ts
// ProjectCard.variants.ts
import { cva } from 'class-variance-authority';

export const projectCardVariants = cva(
  'rounded-lg border border-border bg-card p-4 transition-colors',
  {
    variants: {
      tone: { default: 'hover:bg-accent', muted: 'opacity-70' },
      size: { sm: 'p-3 text-sm', md: 'p-4' },
    },
    defaultVariants: { tone: 'default', size: 'md' },
  },
);
```

```tsx
// ProjectCard.tsx
import { cn } from '@/lib/cn';
import { projectCardVariants } from './ProjectCard.variants';

type ProjectCardProps = {
  title: string;
  tone?: 'default' | 'muted';
  onSelect?: () => void;
};

export function ProjectCard({ title, tone, onSelect }: ProjectCardProps) {
  return (
    <article className={cn(projectCardVariants({ tone }), 'flex flex-col gap-2')}>
      <h3>{title}</h3>
      <button type="button" onClick={onSelect}>…</button>
    </article>
  );
}
```

**Sav vizuelni stil je u `.variants.ts`, i kad komponenta nema varijante** — tada je fajl
samo `cva('…klase…')`. U `.tsx` ostaje `cn(xVariants(...), className)`.
Inline utility klase su dozvoljene **isključivo za layout kompoziciju** (`flex`, `gap`,
`grid`, `max-w`), nikad za vizuelni stil.

### Zašto barrel po komponenti, a ne zbirni

`src/components/index.ts` koji re-eksportuje 40 komponenti ubija tree-shaking i pravi
lančane rebuild-ove u dev-u. **Barrel postoji samo na granici feature-a i na samoj
komponenti — nikad unutar foldera kao zbirni spisak.**

---

## 7. Konvencije imenovanja

| Entitet | Konvencija | Primer |
|---|---|---|
| Folder (ne-komponenta) | `kebab-case` | `user-profile/` |
| Folder komponente | `PascalCase` | `ProjectCard/` |
| Komponenta (fajl + export) | `PascalCase` | `UserCard.tsx` |
| shadcn primitiv | `kebab-case`, flat | `alert-dialog.tsx` |
| Hook | `use` + `camelCase` | `useUserProfile.ts` |
| Slice | `<domain>.slice.ts` | `auth.slice.ts` |
| Selektori | `<domain>.selectors.ts`, export `select*` | `selectCurrentUser` |
| RTKQ API | `<domain>Api.ts` | `authApi.ts` |
| Zod šema | `<name>.schema.ts`, export `*Schema` | `loginSchema` |
| Varijante | `<Ime>.variants.ts`, export `*Variants` | `buttonVariants` |
| Konstante fajl | `<Ime>.constants.ts` | `HeroSection.constants.ts` |
| Tipovi | `types.ts`, **bez `I` prefiksa** | `type User = {}` |
| Test | kolokovan `*.test.ts(x)` | `useAuth.test.ts` |
| E2E | `e2e/*.spec.ts` | `login.spec.ts` |
| Vrednosne konstante | `SCREAMING_SNAKE` | `MAX_UPLOAD_SIZE` |
| Ostali fajlovi | `camelCase` | `storageKeys.ts` |

### Booleani i handleri

| Vrsta | Prefiks | Primer |
|---|---|---|
| boolean state/prop | `is` / `has` / `can` / `should` | `isLoading`, `canSubmit` |
| event handler **prop** | `on` | `onSubmit`, `onSelect` |
| handler **implementacija** | `handle` | `handleSubmit` |
| async akcija | glagol | `login`, `deleteProject` |

### Imenovanje po nameri, ne po tipu

```
✅ formatInvoiceTotal.ts     ❌ helpers.ts
✅ isExpired.ts              ❌ dateUtils.ts
✅ useCheckoutSummary.ts     ❌ useData.ts
```

Ime fajla mora da odgovori na „šta ovo radi" bez otvaranja fajla.

### Zabranjeno

| ❌ | Zašto | ✅ |
|---|---|---|
| `default export` komponente | ime se gubi pri importu, refactor ga ne hvata | imenovani export |
| `utils.ts` / `helpers.ts` / `misc.ts` | kanta koja raste zauvek | ime po poslu |
| `IUser` | mađarska notacija, TS je ne traži | `type User` |
| fajl > **200 linija** | radi previše stvari | podeli |
| komponenta > **150 linija** | isto | izvuci pod-komponentu ili hook |
| magični string/broj | ne može se pretražiti ni promeniti na jednom mestu | imenovana konstanta |

**Jedini izuzetak za `default export`:** lazy route moduli, ako ih router zahteva.

---

## 8. Hookovi — logika ide u hookove, komponente su glupe

Ovo je pravilo o koje se lomi sve ostalo: ako logika živi u hookovima, testiranje je lako,
granice se poštuju, a komponenta ostaje zamenjiva.

1. **Komponenta ne zove `useAppSelector`, `useAppDispatch` ni RTKQ hook direktno.**
   Sve kroz feature hook.
2. **Feature hook je jedini sloj koji zna za Redux.**
3. **Hook vraća objekat sa stabilnim ključevima:** `{ data, isLoading, error, ...akcije }`.
   Nikad niz, osim za `useState`-like API sa tačno dva člana.
4. **Hook nikad ne vraća JSX.** Ako vraća — to je komponenta.
5. **Hook koji radi više stvari se deli.** `useAuth` ≠ `useAuthAndProfileAndSettings`.
6. **Hook koji ne koristi nijedan React hook nije hook** — to je obična funkcija, ide u `lib/`.

```ts
// ✅ features/auth/hooks/useAuth.ts
export function useAuth() {
  const user = useAppSelector(selectCurrentUser);
  const { isLoading } = useGetMeQuery(undefined, { skip: user !== null });

  return { user, isAuthenticated: user !== null, isLoading };
}
```

```tsx
// ✅ komponenta ne zna za Redux
export function UserBadge() {
  const { user, isLoading } = useAuth();
  if (isLoading) return <Spinner />;
  return <span>{user?.name}</span>;
}
```

**Gde koji hook živi:** zna li hook za domen? → `features/<x>/hooks/`.
Ne zna za domen ali zna za app? → `src/hooks/`. Ne zna ni za šta? → `src/hooks/` kao
generički (`useDebounce`, `useMediaQuery`).

---

## 9. State — tri kategorije, svaka ima tačno jedno mesto

| Vrsta | Gde živi | Primer |
|---|---|---|
| **Server state** | RTK Query | lista projekata, profil |
| **Globalni client state** | Redux slice | sesija, tema, jezik, modali |
| **Lokalni UI state** | `useState` / `useReducer` | otvoren dropdown, hover indeks |
| **URL state** | `useSearchParams` | filteri, paginacija, aktivan tab |

Pravila:

1. **Nikad ne kopiraj RTKQ podatke u slice.** Izvedena vrednost iz server podataka je
   `selectFromResult` ili `createSelector` — ne drugi izvor istine koji odmah zastari.
2. **URL je izvor istine za filtere i paginaciju**, ne Redux. Deljiv link, back dugme radi.
3. **`createSlice` uvek.** Ručni reduceri i action konstante ne postoje.
4. **Svaki HTTP poziv ide kroz RTK Query.** `createAsyncThunk` samo za ne-HTTP async.
5. **`createSelector` za sve što izvodi, filtrira ili mapira.** Inline
   `useSelector(s => s.x.list.filter(...))` pravi novi niz svaki render → rerender svaki put.
6. **Kolekcije: `createEntityAdapter`** — normalizacija, `selectById`, sortiranje besplatno.
7. **Najviše 2 `useState` po komponenti.** Treći je signal: `useReducer`, izvedena vrednost,
   ili logika u hook.

```ts
// features/auth/store/auth.slice.ts
const authSlice = createSlice({
  name: 'auth',
  initialState,
  reducers: {
    sessionEstablished(state, action: PayloadAction<AuthUser>) { state.user = action.payload; },
    loggedOut() { return initialState; },
  },
});

export const { sessionEstablished, loggedOut } = authSlice.actions;
export const authReducer = authSlice.reducer;
```

---

## 10. Rutiranje

1. **Svaka ruta je lazy** — bez izuzetka; ruta nosi svoj feature sa sobom.
2. **Guard je wrapper komponenta**, ne `useEffect` + `navigate`.
3. **Query params su izvor istine** za filtere, paginaciju i tabove.
4. **`errorElement` na root nivou**, plus po ruti gde greška ima drugo značenje.
5. **Rute su imenovane konstante** u `src/lib/routes.ts` — nikad string u JSX-u.
6. **Preload na hover** — link zove `route.lazy()` na `onMouseEnter`.

```tsx
// src/routes/router.tsx
export const router = createBrowserRouter([
  {
    path: ROUTES.HOME,
    element: <MainLayout />,
    errorElement: <RootErrorPage />,
    children: [
      { index: true, lazy: () => import('@/pages/LandingPage') },
      { path: ROUTES.PROJECTS, lazy: () => import('@/pages/ProjectsPage') },
      { path: '*', lazy: () => import('@/pages/NotFoundPage') },
    ],
  },
]);
```

```tsx
// src/pages/ProjectsPage.tsx — SAMO kompozicija
import { ProjectFilters, ProjectGrid } from '@/features/projects';

export function Component() {
  return (
    <PageLayout>
      <ProjectFilters />
      <ProjectGrid />
    </PageLayout>
  );
}
Component.displayName = 'ProjectsPage';
```

**`pages/` nema logiku.** Ako page ima `useState` ili `useSelector`, ta logika pripada
feature hooku.

---

## 11. Performanse — tri pravila koja se najčešće krše

### `useEffect` samo za sinhronizaciju sa spoljnim sistemom

Dozvoljen za: pretplatu na browser/DOM/3rd-party event, imperativni DOM rad (focus, scroll,
canvas), setup/teardown ne-React biblioteke, analytics page-view, WebSocket lifecycle.

**Svaki `useEffect` nosi komentar `// effect: <koji spoljni sistem sinhronizuje>`.**

| Anti-pattern | Zamena |
|---|---|
| Fetch podataka | RTK Query hook |
| Derivirani state | izračunaj tokom rendera |
| Reset state-a na promenu prop-a | `key` prop |
| Sinhronizacija dva state-a | jedan izvor istine |
| Logika koja pripada handleru | u handler |
| Slušanje rezultata modala | promise-based `useModal` |
| Inicijalizacija pri startu app-e | module-level kod pre `createRoot` |
| Side-effect na promenu state-a | listener middleware (RTK) |

```ts
// ✅ effect: mousemove na window — svetlo koje prati kursor
useEffect(() => {
  const onMove = (e: MouseEvent) => { … };
  window.addEventListener('mousemove', onMove, { passive: true });
  return () => window.removeEventListener('mousemove', onMove);
}, []);

// ❌ derivirani state
useEffect(() => { setVisible(items.filter((i) => i.tag === tag)); }, [items, tag]);
// ✅
const visible = items.filter((i) => i.tag === tag);
```

### `useMemo` / `useCallback` — tri dozvoljena slučaja

Uz React Compiler ručna memoizacija je uglavnom redundantna, a ponekad se sudara sa
compiler analizom. Dozvoljena je **samo** uz komentar `// memo: <razlog>`:

1. **Skupa kalkulacija** — O(n) ili gore nad kolekcijom, parsiranje u petlji.
2. **Referencijalna stabilnost** za vrednost koja ulazi u dependency array drugog hooka
   ili u context value.
3. **Selector factory** — `useMemo(() => makeSelectItemById(id), [id])`.

```ts
// ✅ memo: sortiranje 5k redova pri svakom keystroke-u
const sorted = useMemo(() => rows.toSorted(byUpdatedAt), [rows]);

// ❌ jeftin izraz — compiler to radi bolje
const fullName = useMemo(() => `${first} ${last}`, [first, last]);
```

Odluka mora biti binarna: **compiler ON i bez ručne memoizacije**, ili compiler OFF i
dosledna ručna memoizacija. Nikad oba.

### Najviše 2 `useState` po komponenti

Eskalacija kad treba više: izvedena vrednost tokom rendera → `useReducer` → feature hook →
URL params → Redux slice.

---

## 12. Modali

**Modal se ne otvara sa `useState(false)`** kad nosi domensku akciju. Ide kroz
`useModal` (promise-based), da poziv izgleda kao obična async funkcija:

```ts
const confirmed = await openModal(ConfirmDeleteModal, { name: project.name });
if (confirmed) await deleteProject(project.id);
```

Prednost nad `useState`-om: nema `useEffect`-a koji „sluša" rezultat, nema state-a koji
mora da se resetuje, i modal je testabilan nezavisno.

`useState(false)` ostaje legitiman samo za čisto vizuelni disclosure (tooltip, accordion).

---

## 13. Stil, i18n, forme, API, testovi — kratka pravila

### Stil
- **Samo semantičke Tailwind klase** (`bg-primary`, `text-muted-foreground`),
  nikad `bg-blue-500` ni `text-[#333]`.
- Tokeni (boje, radijusi, spacing) u `styles/tokens.css`, obe teme.
- `.tsx` sadrži samo layout klase, vizuelni stil u `.variants.ts`.

### i18n
- **Bez literal stringova u UI** — sve kroz `t()`, ključ postoji u **svim** jezicima.
- Format ključa: `feature.section.element` — `auth.login.submitButton`.
  Nikad tekst kao ključ, nikad ključ bez feature prefiksa.
- Prevodi feature-a žive u feature-u; globalni u `src/locales/common.json`.

### Forme
- React Hook Form + zod resolver. Šema u `features/<x>/schemas/<name>.schema.ts`.
- Tip forme se **izvodi iz šeme** (`z.infer`), ne piše ručno.
- Validacija na klijentu nikad nije jedina — server validira istu šemu.

### API
- Jedan `baseApi` sa `injectEndpoints` po feature-u — ne više `createApi` poziva.
- `providesTags` / `invalidatesTags` umesto ručnog refetch-a.
- Tipovi odgovora u `features/<x>/types.ts`.
- **Nikad JWT u `localStorage`** — httpOnly cookie ili memorija.

### Testovi
- **Test je kolokovan** uz fajl koji testira; `__tests__/` samo za integracione testove feature-a.
- Piramida: unit (lib) → hook → komponenta → integracija (MSW) → e2e (Playwright).
- Testira se ponašanje kroz javni API, ne implementacija (`getByRole`, ne `container.querySelector`).

---

## 14. Enforcement — bez ovoga se pravila raspadnu

Najveći rizik nije stek nego **drift**. Pravilo koje se ne proverava mašinski biće prekršeno.

```js
// eslint.config.js — granice slojeva
'import/no-restricted-paths': ['error', { zones: [
  { target: './src/features/*', from: './src/features/*', except: ['./index.ts'] },
  { target: './src/components', from: './src/features' },
  { target: './src/lib',        from: ['./src/features', './src/pages'] },
  { target: './src/features',   from: './src/pages' },
]}],

// zabrane iz §7
'no-restricted-syntax': ['error', {
  selector: 'ExportDefaultDeclaration',
  message: 'Bez default export-a — osim lazy route modula.',
}],
'max-lines': ['error', { max: 200, skipBlankLines: true, skipComments: true }],
'react-hooks/exhaustive-deps': 'error',
```

Dodatno:
- `eslint-plugin-react-hooks` v7 (compiler pravila) kao **error**, ne warning.
- `tsc --noEmit` u CI.
- `size-limit` budžeti po chunku — regresija bundle-a obara build.
- Namerno kršenje granice **obara build**, ne pravi warning.

### Path alias

```json
// tsconfig.json
{ "compilerOptions": { "paths": { "@/*": ["./src/*"] } } }
```

Uvek `@/features/auth`, nikad `../../../features/auth`.

---

## 15. Skripte

```bash
pnpm dev          # Vite dev server
pnpm test         # unit + integracija
pnpm e2e          # Playwright
pnpm lint         # ESLint
pnpm typecheck    # tsc --noEmit
pnpm build        # produkcijski build
pnpm size         # bundle budžeti
pnpm validate     # sve gore — mora proći pre PR-a
```

---

## 16. Anti-patterns — zbirno

| ❌ | Zašto je problem | ✅ |
|---|---|---|
| `src/components/index.ts` sa 40 re-eksporta | ubija tree-shaking, lančani rebuild | barrel po komponenti |
| `features/x/utils.ts` | ime bez značenja, raste zauvek | `features/x/lib/formatPrice.ts` |
| `pages/Dashboard.tsx` sa `useQuery` i tri `useState`-a | logika u pogrešnom sloju | `features/dashboard/hooks/useDashboard.ts` |
| Tailwind klase u `.tsx` | stil razbacan po dva mesta | `.variants.ts` |
| `types/index.ts` sa 300 linija | nema vlasnika, svi ga menjaju | tip uz svoj domen |
| import `@/features/auth/store/auth.slice` | zaobilazi javni API | `useAuth()` iz barrel-a |
| `features/x/components/` puna komponenti koje koriste svi | to više nije feature nego kanta | izdigni u `src/components/` |
| feature koji importuje `pages/` | obrnut smer zavisnosti | page komponuje feature |
| `useEffect` koji fetch-uje | dupli render, race condition, nema keša | RTK Query |
| `useState` za modal sa domenskom akcijom | efekat koji sluša rezultat | promise-based `useModal` |

---

## 17. Kada ova struktura prestane da važi

Feature folders skaliraju do određene tačke. **Preko ~20 feature-a i 5+ developera**
kanonski FSD (`entities` sloj, `steiger` linter) postaje razumniji izbor. Migracija je
izvodljiva upravo zato što su granice već enforce-ovane lintom — radi se mehanički.

Kod ide u zaseban paket (monorepo) **tek kad ga koristi druga aplikacija**. Paket sa jednim
potrošačem je overhead bez koristi: verzionisanje, build korak i PR preko dva foldera.

---

## 18. PR checklist

- [ ] Novi fajl je na najnižem nivou koji ga može držati (feature → components → paket)
- [ ] Feature ne importuje drugi feature (osim javnog barrel-a)
- [ ] `index.ts` feature-a ne eksportuje slice, selektore ni endpointe
- [ ] Komponenta je folder: `.tsx` + `.variants.ts` + `index.ts` (osim u `components/ui/`)
- [ ] U `.tsx` nema Tailwind klasa osim layout utility klasa
- [ ] Komponenta ne zove `useAppSelector` / RTKQ hook direktno
- [ ] Svaki `useEffect` ima `// effect:` komentar i sinhronizuje spoljni sistem
- [ ] Svaki `useMemo`/`useCallback` ima `// memo:` komentar i spada u tri slučaja
- [ ] Najviše 2 `useState` po komponenti
- [ ] Nijedan `default export` osim lazy route modula
- [ ] Nijedan fajl > 200 linija, nijedna komponenta > 150
- [ ] Nema `utils.ts` / `helpers.ts` / magičnih vrednosti
- [ ] Svaki UI string ide kroz `t()`, ključ postoji u svim jezicima
- [ ] Test je pored fajla koji testira
- [ ] Nijedan novi folder ne uvodi terminologiju koje nema u ovom dokumentu
- [ ] `pnpm validate` prolazi
