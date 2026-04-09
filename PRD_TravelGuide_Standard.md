# Product Requirements Document (PRD)

## 1) Product Overview

### 1.1 Product Name
- `TravelGuide` - Smart walking tour app for Vinh Khanh Food Street, District 4, HCMC.

### 1.2 Problem Statement
- Tourists do not have guided context at each POI.
- No human tour guide available at all times.
- Language barrier for international visitors.
- Need offline-first behavior while walking in dense urban area.
- Some users cannot rely on GPS continuously; QR trigger path is required.

### 1.3 Product Vision
- Deliver a seamless, location-aware, multilingual audio guide that works reliably during real-world walking tours.

### 1.4 Success Metrics (MVP)
- 95% of POI entries trigger audio correctly when users enter geofence.
- >= 90% sessions can complete tour without crash.
- >= 80% POI content available in selected language (or safe fallback).
- App cold start to interactive screen < 3s on target Android emulator/device class.

## 2) Scope

### 2.1 In Scope (Current)
- POI discovery and listing.
- Geofence-based auto narration with debounce/cooldown.
- Manual narration via POI detail and audio list.
- QR-based trigger path (non-GPS fallback flow).
- 5-language support: `vi`, `en`, `ja`, `ko`, `zh`.
- Offline local storage using SQLite.
- Admin web for POI/audio/accounts/translations management.

### 2.2 Out of Scope (Current)
- Professionally recorded mp3 library for all POIs.
- Multi-tour package management.
- Advanced analytics dashboard (heatmap, funnel, retention cohorts).
- Enterprise auth/SSO and role hierarchy beyond `admin`/`owner`.

## 3) Users & Personas

### 3.1 Tourist
- Wants hands-free guidance while walking.
- Needs multilingual content.
- May have unstable network and occasional GPS issues.

### 3.2 Shop Owner
- Needs to maintain own POI details/audio quickly.
- Needs a dedicated account and restricted management scope.

### 3.3 Admin
- Owns global data quality and operational health.
- Manages POI lifecycle, translations, audio metadata, and user accounts.

## 4) Functional Requirements

### FR-01: POI Data Management
- App loads POIs from backend API.
- If API unavailable, app falls back to local seed data (`extra_places.json`).
- Fields per POI: names/descriptions by language, coordinates, radius, image, audio URL.

### FR-02: Geofence Narration
- App checks user location against POI radius.
- Debounce = 3 seconds continuous presence.
- Cooldown = 60 seconds before re-triggering same POI.
- On trigger, narration starts via TTS queue engine.

### FR-03: Audio Playback UX
- Queue-based playback to avoid overlapping speeches.
- Mini player visible across pages.
- Support stop / skip.

### FR-04: Multilingual UX
- UI strings from localization resources (`.resx`).
- POI content follows selected app language.
- If translation missing/empty, fallback to Vietnamese text.

### FR-05: Auto Translation (Backend-Centric)
- Backend can auto-fill missing translations using MyMemory.
- Translation is persisted so app/web receive enriched data next requests.
- Translation target can be prioritized by requested language.

### FR-06: QR Trigger Flow
- User scans QR at venue.
- App routes to corresponding POI flow and plays selected language narration.

### FR-07: Admin Web
- Admin can CRUD POIs, manage translation fields, manage accounts.
- Owner accounts exist per shop (business rule excludes gateway/end-point landmarks).

## 5) Non-Functional Requirements

### NFR-01 Reliability
- App must handle offline mode gracefully.
- API failures must not break core local browsing flow.

### NFR-02 Performance
- Geofence checks should not block UI thread.
- Large POI list operations should use in-memory filtering + bounded refresh.

### NFR-03 Security
- Passwords stored as hashes (current implementation uses SHA256).
- Avoid exposing admin-only operations without auth token.
- No secrets in client binaries.

### NFR-04 Maintainability
- Clear service separation: data, geofence, narration, translation.
- API contracts stable and documented.

## 6) System Architecture (Current)

### 6.1 Mobile App
- .NET MAUI app (`TravelGuide`).
- SQLite local persistence.
- Core services:
  - `DatabaseService`
  - `GeofenceEngine`
  - `NarrationEngine`
  - `GpsBackgroundService`

### 6.2 Admin Web
- ASP.NET Core web app (`TravelGuide.AdminWeb`).
- SQLite persistence for POI/users/translations/audio metadata.
- REST APIs for app and web console.

### 6.3 Data Flow
- Admin updates POIs/translations in web.
- App requests POI dataset from API with `lang` hint.
- Backend auto-translates missing language fields when needed.
- App renders localized content with fallback safety.

## 7) API Requirements (High Level)

### 7.1 Auth
- `POST /api/auth/login`

### 7.2 POI
- `GET /api/public/pois?lang={code}`
- `GET /api/pois?lang={code}`
- `POST /api/pois`
- `PUT /api/pois/{id}`
- `DELETE /api/pois/{id}`

### 7.3 Translation
- `GET /api/translations/{id}?lang={code}`
- `PUT /api/translations/{id}`

### 7.4 Account
- `GET /api/accounts`
- `POST /api/accounts`
- `PUT /api/accounts/{id}`
- `DELETE /api/accounts/{id}`

## 8) Acceptance Criteria (MVP)

### AC-01 Language Consistency
- Given user selects any supported language,
- When app loads POIs,
- Then POI title/description are shown in selected language or Vietnamese fallback, never blank.

### AC-02 Geofence Trigger
- Given user enters POI radius and remains >= 3s,
- When cooldown has expired,
- Then narration starts automatically for nearest valid POI.

### AC-03 Web Admin Integrity
- Given admin edits POI core fields,
- When save succeeds,
- Then existing translation fields are preserved unless explicitly edited.

### AC-04 Offline Fallback
- Given API is unreachable,
- When app requests POIs,
- Then app still displays local dataset from seed source.

## 9) Risks & Mitigation

- MyMemory rate limit or unstable response:
  - Mitigation: persistent cache, selective translation by requested language, retry/backoff.
- Android emulator instability (`device offline`):
  - Mitigation: ADB reset procedure and clean emulator profile.
- Inconsistent localization from hardcoded text:
  - Mitigation: centralize custom text mapping and move to resources progressively.

## 10) Release Plan (Recommended)

### Phase 1 (Stabilization)
- Fix build/deploy reliability and language consistency.
- Lock API contracts for POI and translation.

### Phase 2 (Operations)
- Owner-level authorization scope per assigned POI.
- Admin audit logs for content changes.

### Phase 3 (Experience)
- Optional professional audio files and fallback to TTS.
- Analytics dashboard for POI interactions and completion.

