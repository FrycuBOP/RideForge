---
project: RideForge
version: 1
status: draft
created: 2026-08-11
updated: 2026-08-24
prd_version: 1
main_goal: market-feedback
top_blocker: time
---

# Roadmap: RideForge

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

RideForge generuje motocyklową trasę rekreacyjną z punktu startu i preferencji jeźdźca (długość, poziom „krętości"/curviness, charakter fast vs touristic), rysuje ją na mapie i eksportuje jako plik GPX. Odróżnia go od zwykłych map to, że jest **generatorem, nie edytorem** — jeździec przychodzi z gotowym planem, nie z pustą mapą. Kluczowa, najbardziej niepewna teza produktu (*najbardziej ryzykowne założenie — jedyna rzecz, która, jeśli nie zadziała, unieważnia cały produkt*) brzmi: **własny algorytm curviness potrafi wygenerować realnie przejezdną, faktycznie krętą trasę w limicie 30 sekund.**

## North star

**S-01: jeździec generuje trasę z lokalizacji startu i długości przejazdu i widzi ją narysowaną na mapie** — to milestone walidacyjny, bo jako pierwszy przepuszcza własny algorytm razem z zewnętrznym zszywaniem od końca do końca; zgodnie z celem `market-feedback` wystawia realny wynik algorytmu na ocenę najwcześniej, jak pozwalają zależności.

> Gwiazda przewodnia (north star) = najmniejszy kompletny przepływ end-to-end, którego udane dostarczenie dowodzi głównej hipotezy produktu — umieszczony tak wcześnie, jak pozwalają zależności, bo cała reszta ma znaczenie tylko wtedy, gdy to działa.

## At a glance

| ID    | Change ID                | Outcome (user can …)                                              | Prerequisites   | PRD refs                          | Status   |
| ----- | ------------------------ | ---------------------------------------------------------------- | --------------- | --------------------------------- | -------- |
| F-01  | mobile-backend-link      | (foundation) aplikacja Expo dogaduje się z backendem na Railway  | —               | FR-005                            | done |
| F-02  | route-stitching-adapter  | (foundation) backend zamienia waypointy w trasę trzymającą dróg  | —               | FR-005, FR-006, NFR-01            | done |
| S-01  | generate-route-preview   | wygenerować trasę ze startu + długości i zobaczyć ją na mapie     | F-01, F-02      | US-01, FR-001, FR-002, FR-005, FR-006, NFR-01 | in-progress |
| S-02  | curviness-shaping        | ustawić poziom krętości i dostać trasę, która go respektuje      | S-01            | US-01, FR-003                     | proposed |
| S-03  | pace-shaping             | ustawić charakter fast/touristic wpływający na trasę             | S-01            | FR-004                            | blocked  |
| S-04  | gpx-download             | pobrać wygenerowaną trasę jako poprawny plik GPX                 | S-01            | US-01, FR-007                     | proposed |
| S-05  | rider-auth               | założyć konto i zalogować się                                    | F-01            | FR-008                            | proposed |
| S-06  | save-route               | zapisać wygenerowaną trasę na swoim koncie                       | S-05, S-01      | FR-009                            | proposed |
| S-07  | saved-routes-list        | zobaczyć listę swoich zapisanych tras                            | S-06            | FR-010                            | proposed |
| S-08  | poi-waypoints            | dołączyć punkty POI (kawiarnie, widoki, wsie) jako waypointy     | S-01, S-04      | FR-011                            | proposed |
| S-09  | ride-radius-constraint   | ograniczyć obszar przejazdu maksymalnym promieniem od startu     | S-01            | FR-012                            | proposed |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                       | Chain                                                   | Note                                                                 |
| ------ | --------------------------- | ------------------------------------------------------- | ------------------------------------------------------------------- |
| A      | Rdzeń generowania           | `F-02` → `S-01` → `S-02` / `S-04` → `S-03` / `S-08` / `S-09` | Ścieżka gwiazdy przewodniej; `F-01` dołącza przy `S-01`. Priorytet `market-feedback`. |
| B      | Połączenie mobile↔backend   | `F-01` → dołącza do Stream A przy `S-01`                | Cienka wtyczka klient-serwer (loading/error per FR-005), równolegle z `F-02`. |
| C      | Konto i zapisane trasy      | `S-05` → `S-06` → `S-07`                                | Cykl nice-to-have; równolegle z rdzeniem generowania.               |

## Baseline

What's already in place in the codebase as of `2026-08-11` (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** partial — scaffold Expo React Native (`src/app/`: `index`, `explore`, `_layout` na Expo Router); `expo-location` już w zależnościach (istotne dla FR-001). Brak ekranów RideForge.
- **Backend / API:** partial — ASP.NET Core żywy na Railway, ale wyłącznie `GET /health` (`api/Program.cs`). Endpoint generowania nie istnieje.
- **Data:** absent — brak DbContext / ORM / migracji / sterownika DB.
- **Auth:** absent — brak middleware/providera auth w kodzie.
- **Deploy / infra:** present — Railway żywy (`api/Dockerfile`, `api/railway.toml`, `/health` zweryfikowany); mobile przez EAS Build/Submit (per `tech-stack.md`).
- **Observability:** absent — tylko domyślny `ILogger`→stdout łapany przez `railway logs`; brak Sentry/OTel/Serilog.

## Foundations

### F-01: Połączenie mobile ↔ backend

- **Outcome:** (foundation) aplikacja Expo dosięga backendu na Railway typowanym request/response dla jednego endpointu, ze wspólną konwencją stanu ładowania i błędu (per FR-005), zweryfikowaną na `GET /health`.
- **Change ID:** mobile-backend-link
- **PRD refs:** FR-005
- **Unlocks:** S-01 (i każdy późniejszy slice wołający backend); redukuje ryzyko integracyjne dwóch osobnych deployowalnych (Expo ↔ Railway)
- **Prerequisites:** —
- **Parallel with:** F-02
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Minimalna wtyczka, nie „warstwa API" — jeśli spuchnie do generycznego klienta HTTP, złamie zasadę progresywnego ujawniania; trzymać do jednego round-tripu + DTO + konwencji błędu, resztę dokłada S-01.
- **Status:** done

### F-02: Adapter zszywania trasy (server-side)

- **Outcome:** (foundation) backend zamienia uporządkowaną listę waypointów w trasę trzymającą się dróg (polilinia + dystans + czas) przez zewnętrzne commodity directions/map-matching API, z kluczem trzymanym server-side.
- **Change ID:** route-stitching-adapter
- **PRD refs:** FR-005, FR-006, NFR-01
- **Unlocks:** S-01 (i pochodne slice'y kształtujące trasę S-02, S-03) — to zewnętrzna zależność, do której algorytm curviness podaje waypointy
- **Prerequisites:** —
- **Parallel with:** F-01
- **Blockers:** —
- **Unknowns:**
  - Kształt outputu algorytmu krętości: rzadkie, uporządkowane waypointy vs gęsty ślad? — Owner: user. Block: yes (dla wyboru wzorca) — determinuje directions-z-waypointami (np. OpenRouteService) vs map-matching (OSRM/GraphHopper). Porównanie i kryterium: `context/changes/route-stitching-adapter/research-stitching.md`.
  - Który dostawca directions/map-matching (GraphHopper Directions / OpenRouteService / Mapbox / self-hosted OSRM)? — Owner: user. Block: no (curviness liczymy sami, więc każde API trzymające dróg wystarczy — patrz PRD Open Question 2, rozwiązane 2026-08-11). Zależny od kształtu outputu algorytmu (wyżej).
- **Risk:** Round-trip do zewnętrznego API wlicza się w limit 30 s (NFR-01); przy wielu waypointach latencja się kumuluje. Adapter musi być swappable, by zmiana dostawcy nie dotknęła algorytmu.
- **Status:** done

## Slices

### S-01: Generowanie trasy z podglądem na mapie (north star)

- **Outcome:** jeździec wpisuje lokalizację startu i długość przejazdu, klika Generuj i widzi trasę narysowaną na mapie, wychodzącą z podanego startu.
- **Change ID:** generate-route-preview
- **PRD refs:** US-01, FR-001, FR-002, FR-005, FR-006, NFR-01
- **Prerequisites:** F-01, F-02
- **Parallel with:** S-05
- **Blockers:** —
- **Decyzja (mapa):** biblioteka mapy = **`react-native-maps`** (`<Polyline>` + `fitToCoordinates` pod outcome). Odrzucono `expo-maps` (alpha, iOS 18+, brak Expo Go). Research: `context/changes/route-stitching-adapter/research-stitching.md`, pamięć `northstar-tech-decisions`.
- **Prerequisite (techniczny):** `react-native-maps` nie działa w Expo Go → S-01 wymaga **dev buildu (EAS)**, nie Expo Go.
- **Unknowns:**
  - Czy własny algorytm generuje waypointy tak, że po zszyciu trasa mieści się w ±20% zadanej długości (kryterium akceptacji US-01)? — Owner: user. Block: no (to rdzeń do zbudowania i zmierzenia, nie decyzja blokująca planowanie).
- **Risk:** To najcięższy i najbardziej niepewny slice (nowatorski algorytm + limit 30 s). Sekwencjonowany pierwszy mimo wagi, bo jako north star wystawia najbardziej ryzykowne założenie na ocenę najwcześniej — zgodnie z celem `market-feedback`.
- **Status:** in-progress

### S-02: Kształtowanie krętości (curviness)

- **Outcome:** jeździec ustawia suwak krętości i dostaje trasę, której geometria realnie odzwierciedla wybrany poziom (prosto → bardzo kręto).
- **Change ID:** curviness-shaping
- **PRD refs:** US-01, FR-003
- **Prerequisites:** S-01
- **Parallel with:** S-04, S-05
- **Blockers:** —
- **Unknowns:**
  - Jak zmierzyć i zweryfikować, że wyższy suwak = obiektywnie bardziej kręta trasa (metryka krętości)? — Owner: user. Block: no.
- **Risk:** To właściwy różnicownik produktu — jeśli suwak nie zmienia odczuwalnie trasy, teza produktu upada. Zaraz po north star, bo tu żyje najbardziej ryzykowne założenie.
- **Status:** proposed

### S-03: Kształtowanie charakteru jazdy (fast/touristic)

- **Outcome:** jeździec wybiera charakter fast lub touristic, a generowanie odpowiednio dobiera drogi.
- **Change ID:** pace-shaping
- **PRD refs:** FR-004
- **Prerequisites:** S-01
- **Parallel with:** S-02, S-04
- **Blockers:** —
- **Unknowns:**
  - Co konkretnie znaczy „fast" vs „touristic" — zarówno w UI (definicja dla jeźdźca), jak i w wagach algorytmu? — Owner: user. Block: yes (PRD Open Question 1; bez tej definicji FR-004 nie jest shippable).
- **Risk:** Zablokowany do czasu zdefiniowania semantyki fast/touristic. Izolowany od S-02, żeby otwarte pytanie nie wstrzymywało krętości (właściwego różnicownika).
- **Status:** blocked

### S-04: Pobieranie GPX

- **Outcome:** jeździec pobiera wygenerowaną trasę jako plik GPX, sensownie nazwany, który poprawnie otwiera się w mainstreamowych aplikacjach.
- **Change ID:** gpx-download
- **PRD refs:** US-01, FR-007
- **Prerequisites:** S-01
- **Parallel with:** S-02, S-03, S-05
- **Blockers:** —
- **Unknowns:**
  - Czy generowany GPX faktycznie ładuje się w Garmin, Google Maps i OsmAnd (guardrail — zepsuty GPX to twarda regresja)? — Owner: user. Block: no (do przetestowania, nie decyzja).
- **Risk:** GPX to faktyczny efekt, który jeździec zabiera ze sobą; równoległy do kształtowania trasy, bo niezależny od curviness/pace. Guardrail poprawności GPX trzeba przetestować na realnych urządzeniach.
- **Status:** proposed

### S-05: Konto i logowanie jeźdźca

- **Outcome:** jeździec zakłada konto i loguje się (wprowadza minimalny scaffold auth przy pierwszym slice'ie, który go potrzebuje).
- **Change ID:** rider-auth
- **PRD refs:** FR-008
- **Prerequisites:** F-01
- **Parallel with:** S-01, S-02, S-03, S-04
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Nice-to-have (drugorzędne Kryterium sukcesu); sekwencjonowany po ścieżce koniecznej, bo `top_blocker: time` każe najpierw domknąć rdzeń generowania. Nie parkowany, bo to realny drugorzędny cel produktu.
- **Status:** proposed

### S-06: Zapis wygenerowanej trasy

- **Outcome:** zalogowany jeździec zapisuje wygenerowaną trasę na swoim koncie (ten slice wprowadza persystencję — pierwszy moment, w którym jest potrzebna).
- **Change ID:** save-route
- **PRD refs:** FR-009
- **Prerequisites:** S-05, S-01
- **Parallel with:** S-02, S-03, S-04
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Wymaga tożsamości (S-05) i wygenerowanej trasy do zapisania (S-01). Warstwa danych pojawia się dopiero tu, zgodnie z progresywnym ujawnianiem, zamiast osobnego foundation.
- **Status:** proposed

### S-07: Lista zapisanych tras

- **Outcome:** zalogowany jeździec widzi listę swoich zapisanych tras i może wrócić do wcześniejszego przejazdu.
- **Change ID:** saved-routes-list
- **PRD refs:** FR-010
- **Prerequisites:** S-06
- **Parallel with:** S-08, S-09
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Konsumuje persystencję z S-06; bez czego zapisać, nie ma czego listować, stąd kolejność po S-06.
- **Status:** proposed

### S-08: Punkty POI jako waypointy

- **Outcome:** jeździec włącza dołączanie punktów POI (kawiarnie, punkty widokowe, wsie), które trafiają na trasę i do pliku GPX jako waypointy.
- **Change ID:** poi-waypoints
- **PRD refs:** FR-011
- **Prerequisites:** S-01, S-04
- **Parallel with:** S-02, S-03, S-09
- **Blockers:** —
- **Unknowns:**
  - Które źródło danych POI (OSM Overpass API / Google Places / Foursquare)? — Owner: user. Block: no (PRD Open Question 3; FR-011 jest nice-to-have).
- **Risk:** Nice-to-have; zależy od gotowego generowania (S-01) i eksportu GPX (S-04), bo POI muszą trafić i na trasę, i do pliku.
- **Status:** proposed

### S-09: Ograniczenie promienia przejazdu

- **Outcome:** jeździec opcjonalnie ustawia maksymalny promień od punktu startu, by ograniczyć obszar generowanej trasy.
- **Change ID:** ride-radius-constraint
- **PRD refs:** FR-012
- **Prerequisites:** S-01
- **Parallel with:** S-08, S-02, S-03
- **Blockers:** —
- **Unknowns:**
  - Czy ograniczenie obszaru powinno być must-have (runda Socratesa przy FR-001 sugerowała, że sam start może nie wystarczać)? — Owner: user. Block: no (PRD Open Question 4; obecnie nice-to-have).
- **Risk:** Rozszerza S-01 o constraint obszaru; niski priorytet dopóki OQ4 nie przesądzi, że to must-have.
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID                | Suggested issue title                                   | Ready for `/10x-plan` | Notes                                            |
| ---------- | ------------------------ | ------------------------------------------------------- | --------------------- | ------------------------------------------------ |
| F-01       | mobile-backend-link      | Wire Expo app to Railway backend (typed client + errors) | yes                   | Równoległy z F-02; odblokowuje north star S-01   |
| F-02       | route-stitching-adapter  | Server-side directions/map-matching stitching adapter   | yes                   | Wybór dostawcy niezablokowany; odblokowuje S-01  |
| S-01       | generate-route-preview   | Generate route from start + length, show on map         | no                    | Czeka na F-01 + F-02                             |
| S-02       | curviness-shaping        | Curviness slider shapes the generated route             | no                    | Czeka na S-01                                    |
| S-03       | pace-shaping             | Fast/touristic pace shapes the generated route          | no                    | Zablokowany na OQ1 (definicja fast/touristic)    |
| S-04       | gpx-download             | Download generated route as valid GPX                   | no                    | Czeka na S-01; przetestować w Garmin/OsmAnd      |
| S-05       | rider-auth               | Rider account creation and login                        | no                    | Nice-to-have; czeka na F-01                      |
| S-06       | save-route               | Save a generated route to the account                   | no                    | Wprowadza persystencję; czeka na S-05 + S-01     |
| S-07       | saved-routes-list        | List saved routes                                       | no                    | Czeka na S-06                                    |
| S-08       | poi-waypoints            | Include POI waypoints in route and GPX                  | no                    | Nice-to-have; OQ3 (źródło POI); czeka na S-01+S-04 |
| S-09       | ride-radius-constraint   | Optional max radius from start                          | no                    | Nice-to-have; OQ4 (czy must-have); czeka na S-01 |

## Open Roadmap Questions

1. **Co konkretnie znaczy „fast" vs „touristic"?** (definicja w UI + wagi algorytmu) — Owner: user. Block: `S-03`.
2. **Które źródło danych POI dla FR-011?** (OSM Overpass / Google Places / Foursquare) — Owner: user. Block: `S-08` (miękko — nice-to-have).
3. **Czy ograniczenie promienia/obszaru powinno być must-have?** — Owner: user. Block: `S-09` (miękko — obecnie nice-to-have; decyzja może podnieść priorytet).

(Rozwiązane 2026-08-11: PRD Open Question 2 „które API wspiera curviness" → hybryda, własny algorytm + swappable directions API. Rezydualny wybór dostawcy żyje jako Unknown w `F-02`.)

## Parked

- **Turn-by-turn / nawigacja na żywo** — Why parked: PRD §Non-Goals — permanentny non-goal, nie odroczenie. RideForge eksportuje trasy, nie prowadzi po nich.
- **Funkcje społecznościowe (współdzielenie tras, biblioteka publiczna, obserwowanie)** — Why parked: PRD §Non-Goals — permanentny non-goal; v1 to narzędzie osobiste.
- **Narracyjny opis charakteru przejazdu** — Why parked: świadomie odłożone do v2 (shape-notes: „ride description narrative deferred to v2"); v1 obejmuje generowanie + GPX.

## Done

- **F-01: (foundation) aplikacja Expo dosięga backendu na Railway typowanym request/response dla jednego endpointu, ze wspólną konwencją stanu ładowania i błędu (per FR-005), zweryfikowaną na `GET /health`.** — Archived 2026-08-16 → `context/archive/2026-08-11-mobile-backend-link/`. Lesson: —.
- **F-02: (foundation) backend zamienia uporządkowaną listę waypointów w trasę trzymającą się dróg (polilinia + dystans + czas) przez zewnętrzne commodity directions/map-matching API, z kluczem trzymanym server-side.** — Archived 2026-08-16 → `context/archive/2026-08-16-route-stitching-adapter/`. Lesson: —.
