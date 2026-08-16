# Research: zszywanie waypointów w trasę po drogach (F-02)

> Status: **research / decyzja odłożona**. Change nie jest jeszcze formalnie otwarty
> (`/10x-new route-stitching-adapter`). Źródło: dokumentacja dostawców pobrana przez
> Context7 (2026-08-16). Pokrywa *co* API potrafią — **nie** pokrywa pricingu,
> rate-limitów ani jakości tras motocyklowych (do weryfikacji osobno).

## Kontekst

F-02 odblokowuje north star **S-01 (`generate-route-preview`)**. Architektura jest
hybrydowa (`tech-stack.md`, PRD Open Question 2 rozstrzygnięte 2026-08-11): **nasz
własny algorytm krętości** produkuje kandydatów (waypointy / ślad) po stronie serwera,
a **zewnętrzne, wymienne API** tylko zszywa je w trasę jadącą po realnych drogach.
Klucz do API zostaje po stronie backendu (.NET / Railway), nie w kliencie.

**Dlaczego decyzja jest odłożona:** wybór dostawcy i API zależy od tego, **co
emituje nasz algorytm**, a algorytmu jeszcze nie ma. Ta notatka utrwala kryterium
wyboru, żeby decyzję dało się podjąć od razu, gdy kształt outputu będzie znany.

## Dwa wzorce (to jest właściwa decyzja)

### A. Directions z waypointami
Podajesz **uporządkowaną listę punktów**, API liczy trasę **przez** nie i zwraca
geometrię.

- **Dobre, gdy** algorytm emituje **rzadkie, uporządkowane waypointy** (np. kilka–
  kilkanaście punktów kontrolnych), a resztę trasy ma dobrać silnik po drogach.
- **Przykład API:** OpenRouteService `POST /v2/directions/{profile}` — body z tablicą
  `coordinates`, zwraca `routes[].geometry` (encoded polyline lub GeoJSON) +
  `way_points` (indeksy) + `summary.distance/duration`. Profile m.in. `driving-car`,
  `cycling-*`.
- **Konsekwencja:** trasa "słucha" naszych punktów, ale między nimi to silnik decyduje
  o drogach — mniejsza kontrola nad kształtem w segmencie.

### B. Map-matching (snapowanie śladu)
Podajesz **gęsty, potencjalnie zaszumiony ślad**, API **przyciąga** go do sieci dróg
(Hidden Markov Model + Viterbi).

- **Dobre, gdy** algorytm emituje **gęsty ślad / geometrię**, którą chcemy tylko
  "docisnąć" do dróg (zachowuje nasz kształt wierniej niż directions).
- **Przykłady API:**
  - **OSRM** `match` — `coordinates` + opcjonalne `radiuses` (dokładność GPS),
    `geometries=geojson`, `gaps=split|ignore`, `tidy`, `waypoints` (indeksy punktów
    traktowanych jako waypointy; muszą zawierać pierwszy i ostatni).
    ⚠️ Przy **dużych przerwach między punktami** dzieli wynik na **pod-ślady** i może
    odrzucać outliery — ryzykowne przy rzadkich punktach.
  - **GraphHopper** `/match?profile=car` — POST GPX/JSON, HMM + Viterbi;
    `snap_prevention` (tunnel/bridge/ferry/motorway/trunk/ford), custom model do
    sterowania, których dróg unikać.
- **Konsekwencja:** wierniej oddaje nasz kształt (dobre dla krętości!), ale wymaga
  gęstszego wejścia i jest wrażliwe na rzadkie/nietypowe punkty.

## Kryterium wyboru (do rozstrzygnięcia po powstaniu algorytmu)

| Output algorytmu krętości | Wzorzec | Kandydat na start |
| --- | --- | --- |
| Rzadkie, uporządkowane waypointy | **A. Directions z waypointami** | OpenRouteService directions |
| Gęsty ślad / geometria do dociśnięcia | **B. Map-matching** | OSRM `match` lub GraphHopper `/match` |

Krętość to rdzeń produktu (różnicownik z S-02), więc jeśli algorytm potrafi wygenerować
gęsty ślad, **map-matching (B) prawdopodobnie wierniej odda zamierzony kształt** niż
directions, które mogą "wyprostować" trasę między rzadkimi punktami. To hipoteza do
sprawdzenia, nie decyzja.

## Self-hosting vs SaaS

| Dostawca | Silnik | Self-host | Uwaga |
| --- | --- | --- | --- |
| OSRM | C++ | tak (OSS) | najszybszy, ale `match` dzieli przy przerwach |
| GraphHopper | Java | tak (OSS) + API komercyjne | `snap_prevention` + custom model = kontrola dróg |
| OpenRouteService | Java | tak (OSS) + free tier | directions z waypointami, GeoJSON out-of-the-box |
| Mapbox | — | **nie** (tylko SaaS) | komercyjny; Directions + Map Matching API |

- **Na start (MVP):** hostowany free tier (ORS / GraphHopper Directions API) — najmniej
  infrastruktury, szybciej do north star.
- **Plan B:** self-host OSRM/GraphHopper, gdy limity/koszty zaczną uwierać albo gdy
  potrzebna pełna kontrola nad profilem/kosztami.
- Wymienność dostawcy jest wymogiem architektury (adapter), więc pierwszy wybór nie
  jest nieodwracalny.

## Otwarte pytania do domknięcia przed planem F-02

1. **Kształt outputu algorytmu:** rzadkie waypointy czy gęsty ślad? → determinuje A vs B.
   (Owner: user; blokujące dla wyboru API.)
2. **Pricing / rate-limity** wybranego dostawcy vs oczekiwany ruch MVP. (Nie z Context7.)
3. **Jakość tras** dla motocyklowego use-case'u na realnym regionie testowym. (Nie z Context7.)
4. Format geometrii do klienta: encoded polyline vs GeoJSON (klient używa
   `react-native-maps` `<Polyline coordinates={[{latitude, longitude}]}>`).
