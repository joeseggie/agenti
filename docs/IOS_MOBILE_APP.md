# iOS companion application

## Architecture

`mobile` is a TypeScript React Native application using Expo SDK 58 and Expo
Router. It calls the **existing ASP.NET Core backend** and PostgreSQL database.
No new backend host, database, Azure resource, CORS policy, or .NET solution
project is introduced. `EastSeat.Agenti.Android` remains the independent MAUI
Android client; the pre-existing `EastSeat.Agenti.iOS` directory is not used.

Routes live in `mobile\src\app`; screens, API contracts, sessions and theme live in
sibling directories. Zod validates responses at the network boundary. TanStack
Query keeps screen data in memory only and is cleared on authentication changes.
Native stack/tab navigation, native Expo buttons, dynamic text, semantic colors,
and pull-to-refresh provide the iPhone UI. Controlled React Native text fields
provide explicit accessibility labels and preserve unsaved financial input.

Use [mobile setup](../mobile/README.md) for the exact Windows and npm commands.
Node 26.13.0, Expo 58.0.19, and the npm lockfile are the supported baseline.

## Authentication and isolation

1. `POST /api/auth/login` accepts `{ "email": "...", "password": "..." }`.
2. The existing response includes `accessToken`, `tokenType: "Bearer"`,
   `expiresIn` (seconds), `userId`, `email`, `fullName`, `role`, `agentId`, and
   `branchId` inside the standard response envelope.
3. The client saves **only** access token, absolute expiry, and API URL using
   `expo-secure-store`, with `WHEN_UNLOCKED_THIS_DEVICE_ONLY`. Passwords and
   personal profile data are not persisted. No token is stored in AsyncStorage,
   configuration, logs, or URLs.
4. Startup blocks protected navigation until SecureStore hydration and
   `GET /api/auth/me` succeed. A network failure offers retry or clearing the saved
   session; it never displays cached protected content as an authenticated
   fallback. A different API URL invalidates the old session before sending it.
5. Expiry is checked on a timer and when the app becomes active. Protected `401`
   responses clear storage and cache and return to sign-in; `403` reports denied
   access without discarding a valid token. Late responses cannot invalidate a
   newer login. SecureStore failures are visible and do not silently succeed.
6. Sign-out clears this device's session and cached data. There is **no refresh
   token or server logout/revocation endpoint**. A copied JWT remains usable until
   expiry unless its account is disabled/deleted or its role changes. The server
   checks those conditions on every bearer-token validation.

The existing JWT issuer, audience (including the default
`EastSeat.Agenti.Android` audience), HMAC key, and configurable
`Jwt:ExpiryMinutes` remain unchanged. Default lifetime is 60 minutes. iOS does
not need a separate key or audience. JWT library server-side clock skew remains
unchanged; the client conservatively stops using a token at its recorded expiry.
Keychain data can survive iOS reinstallation, so restoration always revalidates
it with the server.

The API accepts **bearer authentication only**, including privileged policies.
An admin browser cookie cannot elevate an agent bearer token. Resource scope is
loaded from the current active database user and agent via `IBankingAccessService`;
client-supplied or stale JWT branch/agent IDs are not trusted. User/agent branch
assignments must agree.

Agents see only their own agent, wallets and session count summaries. Supervisors
and admins see their assigned branch, not arbitrary branches. Cash-count saves
use the authenticated agent and validate wallet ownership, active status and
duplicates. Cross-branch IDs are denied before operations run. Vault access is
Admin/Supervisor; approval/rejection is Admin only. The existing service enforces
dual approval, expiry, required notes and financial posting rules.

These API scope restrictions are intentional for both mobile clients. Existing
Blazor service calls retain their previous unfiltered behavior; the optional
service query filters are supplied by authenticated API endpoints.

## API contracts used by the first release

All paths below are relative to the configured URL ending in `/api`. Responses
are JSON with camelCase names:

```json
{ "success": true, "data": {}, "error": null }
```

Failures ordinarily use `{ "success": false, "data": null, "error": "..." }`.
Authentication challenges may have an empty body. `400` is validation/business
rules, `401` missing/invalid/expired authentication, `403` insufficient role or
scope, `404` a missing accessible resource, `409` a conflicting banking update,
and `5xx` an unavailable server. Invalid or inaccessible resource identifiers may
return `403` rather than disclosing existence. The client also reports timeouts,
offline errors, invalid JSON and incompatible DTOs. It never retries mutations
automatically.

| Endpoint | Contract / behavior | Access |
| --- | --- | --- |
| `POST /auth/login` | `LoginRequest` -> `LoginResponse`; existing JWT contract | Anonymous |
| `GET /auth/me` | `SessionUserResponse`: current identity, role and assignments, no new token | Bearer user |
| `GET /dashboard` | `DashboardViewModel`, wallet rows, session status and balance basis | Own wallets for agents; branch for managers |
| `GET /agents` | `AgentListItemDto[]`, ordered by code | Own agent or assigned branch |
| `GET /agents/{id}` | `AgentDetailDto`, nested wallets | Same scope |
| `GET /cash-sessions` | `CashSessionListItemDto[]`, newest first; optional `branchId` must equal assigned branch | Own participation or assigned branch |
| `GET /cash-sessions/{id}` | `CashSessionDetailDto`, nested per-agent count/wallet summaries | Same scope; agent totals exclude peers |
| `GET /cash-counts/current` | `CurrentSessionDto`, server-provided operation flags and count IDs | Current user |
| `GET /cash-counts/initialize?isOpening=true/false` | `CashCountFormModel` | Active own agent |
| `GET /cash-counts/{id}` | Existing own form, with `isReadOnly` where applicable | Own agent |
| `POST /cash-counts` | Save `CashCountFormModel` -> `CashCountSaveResult` containing count/session IDs | Own active wallets |
| `POST /cash-counts/{id}/submit` | Submit saved count -> `CashCountSaveResult` | Own agent |
| `GET /vault/{branchId}` | `VaultDto`: branch name and current balance | Admin/Supervisor, own branch |
| `GET /vault/{branchId}/transactions?take=50` | `VaultTransactionListItemDto[]`, newest first | Same scope |
| `POST /vault/{branchId}/adjustment` | `{ amount, isDeposit, notes }` -> `VaultOperationResult` | Admin/Supervisor, own branch |
| `POST /vault/adjustment/{id}/approve` or `/reject` | `VaultOperationResult` | Admin, own branch; no self-approval |

Lists retain the existing array contract: **no cursor, page or total-count
pagination** was invented. Vault history accepts `take` from 1 through 200
(default/mobile 50) and `includeExpired` (default false). The app explicitly labels
this as recent activity, not complete history. Existing wallet type and
administrative endpoints remain available but are not added to the iOS UI.

Database enums are strings; these existing JSON DTOs serialize enum values as
numbers. Cash count states are Draft=0, PendingApproval=1, Approved=2, Rejected=3.
Session workflows are Legacy=1 and BankReconciliation=2. `DateOnly` values are
`YYYY-MM-DD`; observation/audit timestamps are ISO 8601 with a timezone.

Cash count forms retain server wallet IDs and expected balances. Users enter
amounts or denomination quantities, explanation and observation timestamps;
"Observed now" explicitly records the user's chosen observation time. Banking
workflow counts require per-wallet observation times and are validated by the
server. Legacy counts retain the existing behavior. The UI limits amount entry
to 12 whole digits and 2 decimals to avoid unsafe JavaScript number serialization;
financial calculations and persistence remain .NET `decimal`.

Each editing visit waits for a successful fresh form fetch before initializing
the editor; a failed refresh cannot expose an editable cached form. Subsequent
cache refreshes preserve unsaved input in the active editor.

Save and submit are separate API operations. The returned count ID is retained
before submitting. A definitive submission validation failure (`400`) preserves
the draft and restores editing so the user can correct it. An ambiguous failure
locks editing and retries submission using the saved ID without saving again.
Both legacy and bank-reconciliation submission replays return the existing result
for pending or approved counts without repeating financial postings or audit
entries. Vault request timeouts are ambiguous too: the app requires checking recent
activity before enabling another request, because the existing adjustment API has
no idempotency key.

## Configuration and infrastructure

Set `EXPO_PUBLIC_API_URL` in ignored `mobile\.env` for Expo Go. For EAS, create it
in the `development` and `preview` environments; `eas.json` selects those
environments explicitly. It is compiled into JavaScript, so **never** put an API
key, database connection string, JWT signing key or password in any `EXPO_PUBLIC_`
variable.

The backend needs its existing HTTPS configuration and `Jwt__Key`. No new Azure
settings are required. Native networking is not subject to browser CORS; do not
add wildcard origins. Requests omit cookies and reject redirects. No automatic
switch to HTTP, certificate-validation bypass, alternate backend, or offline
financial queue exists.

## Internal iOS distribution

| EAS profile | Distribution | Environment | Runtime |
| --- | --- | --- | --- |
| `development` | Internal/ad hoc | `development` | Development client, connects to Metro |
| `internal` | Internal/ad hoc | `preview` | Embedded release JS, standalone |

Both profiles use Node 26.13.0. EAS CLI 27.1.0 and remote build numbers are
configured; `internal` auto-increments. The app uses bundle ID
`com.eastseat.agenti.mobile` and scheme `agenti`. No `submit` or store profile
exists. Native project generation happens on EAS macOS builders, not Windows.

An operator must authenticate to Expo, link an Expo project, select the authorized
Apple team, register the test iPhone, and create signing credentials/provisioning
through EAS. Apple Developer membership and EAS plan limits apply. Keep signing
material in EAS credential management, not `.env`, GitHub Actions, or Git.
`EXPO_TOKEN` is only needed if an explicitly authorized non-interactive build
pipeline is added later; the supplied CI needs no Expo or Apple secrets.

Use `npm run build:internal`, open its successful installation URL in Safari,
and install on the device included in that build's ad hoc profile. Adding a device
requires rebuilding or re-signing; Apple can delay new device eligibility. A
successful cloud build alone does not prove installation or login.

Official references: [internal distribution](https://docs.expo.dev/build/internal-distribution/),
[EAS environments](https://docs.expo.dev/eas/environment-variables/usage/),
[SecureStore](https://docs.expo.dev/versions/v58.0.0/sdk/securestore/).

## Validation and release gates

The mobile workflow in `.github\workflows\mobile.yml` installs the lockfile,
checks formatting/lint/types, runs Jest and exports an iOS bundle. The existing
`.NET` CI remains responsible for web/Android builds and all backend suites.
`MobileApiTests` exercises real JWT middleware, API JSON, branch/role isolation,
forged wallet IDs, revoked account access, cash count submission and vault dual
approval against isolated PostgreSQL. Mobile component tests mock native hosts,
so they do not verify VoiceOver, keyboard positioning or signing.

Run the commands in `mobile\README.md`, then from the root:

```powershell
dotnet build
dotnet test --filter "Category!=AndroidDevice"
```

`Category=AndroidDevice` requires the existing Android device test setup.

Before closing issue #100, record the **actual** EAS build ID and physical-device
results in the issue or pull request (do not expose UDIDs or signing material):

- Build installs and launches on the registered iPhone with no Metro connection.
- Sign in with an authorized test account, restart, restore, sign out, and confirm
  Back/deep links cannot reopen protected screens.
- Verify dashboard, agent/session details, count draft/save/submit and authorized
  vault activity against the configured backend.
- Verify a forbidden role and foreign branch cannot read or mutate protected
  records; expired/disabled/invalid tokens return to sign-in.
- Exercise offline startup, failed saves followed by retry, ambiguous vault
  requests, large system text, VoiceOver labels, keyboard reachability, and back
  navigation.
- Confirm the existing Blazor and MAUI Android clients still use the same API
  contracts and expected branch permissions.
- Resolve the upstream Expo Doctor version-metadata mismatch documented in the
  mobile README; do not count a local manifest check as a clean online Doctor run.

Physical iPhone and signed-build validation cannot be substituted with a Jest
result or a JavaScript export. No successful EAS build/device installation is
claimed by these configuration files.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| Configuration screen | Set a valid HTTPS URL ending in `/api`, restart Metro/rebuild the embedded app |
| Network failure on iPhone | Check that the phone can reach the host and trust its HTTPS certificate; PC `localhost` is not reachable as phone `localhost` |
| Restore fails with `404` | Deploy the backend version containing `/api/auth/me` |
| Sign-in works, resources return `403` | Check active agent, branch and role assignments on the server; user and agent branches must agree |
| Secure storage cannot be cleared | Unlock the device and use **Clear saved session** again; in-memory authentication is already removed |
| Count cannot be changed | Refresh current count state; approved/settled counts and bank review corrections belong in the web workspace |
| EAS reports not logged in/unlinked project | Run the documented `login` and `init` commands from `mobile` |
| Build won't install on one phone | Verify that device was in the provisioning profile when this build was signed; rebuild/re-sign if necessary |
| Expo Doctor asks for unpublished packages | Keep the locked SDK baseline; do not force peers or disable checks; rerun after the registry catches up |
