# Agenti iPhone companion

Expo Router + TypeScript client for the existing Agenti `/api` backend. This is an
independent iOS project, not part of `Agenti.slnx`. The MAUI Android app is unchanged.
Distribution is **internal/ad hoc**, not App Store or TestFlight.

## Windows 11 setup

Install Git and **Node.js 26 LTS, 26.13.0** (the version in `.nvmrc`). Use npm 11;
the lockfile was generated with npm 11.20.0. No Mac, Xcode, Android Studio, or local
.NET installation is needed when using an existing HTTPS backend.

```powershell
Set-Location mobile
npm ci
Copy-Item .env.example .env
# Edit .env: EXPO_PUBLIC_API_URL=https://your-existing-agenti-host/api
npm run ios
```

Install **Expo Go** on your iPhone and scan the terminal QR code with the iPhone
camera. Both devices must reach the development server. Allow Node on the Windows
private-network firewall and use the same Wi-Fi. `npm run ios` explicitly uses
Expo Go; it does not launch a local iOS simulator. If the network blocks LAN
discovery, use `npx expo start --go --tunnel` and follow its tunnel-tool prompt.

Expo SDK **58** is required. The app pins Expo **58.0.19**, React 19.3.0 and React
Native 0.88.0, with compatible packages recorded in `package-lock.json`. Do not
upgrade individual native packages arbitrarily. If the App Store's Expo Go no
longer supports SDK 58, use an internal development build or deliberately upgrade
the SDK and revalidate.

The API URL is public build-time configuration, not a secret. It must end in
`/api`, have no embedded credentials, and use HTTPS for non-loopback hosts. A
physical iPhone's `localhost` is the iPhone, not your PC. Prefer the existing
deployed HTTPS backend or a trusted HTTPS development endpoint; no permissive ATS
or CORS exception is included. Restart Expo after changing `.env`.

Deploy the backend changes from this branch before using this client: session
restoration requires `GET /api/auth/me`, and the existing read endpoints now
enforce branch and agent scope.

## Checks

```powershell
npm run format:check
npm run lint
npm run typecheck
npm test
npm run export:ios
npx --yes expo-doctor@1.21.4
```

`npm run format` applies formatting. Tests cover networking, SecureStore session
lifecycle, count input conversion, sign-in retry, and preserving failed saves.
They use mocked native controls; they do not establish physical-device behavior.
GitHub Actions runs the reproducible checks and an iOS JavaScript export without
Apple credentials. An export is **not** a signed iOS app.

### Expo compatibility metadata

At implementation time Expo's online compatibility service recommended SDK 58
patches not yet available in the npm registry (including `@expo/ui` 58.0.27 and
`jest-expo` 58.0.14). The published SDK's own manifest accepts the locked set:

```powershell
$env:EXPO_OFFLINE = "1"
npx expo install --check
Remove-Item Env:EXPO_OFFLINE
```

This local compatibility check is not a replacement for Expo Doctor. Doctor's
online version check remains a release gate until registry and compatibility
metadata agree. Do not suppress it with exclusions or force incompatible peers.
Once those versions are published, use `npx expo install --fix`, inspect the
lockfile changes, and rerun all checks. No check is disabled in project config.

## EAS build and install

EAS CLI is pinned to **27.1.0** in commands, not installed as an app dependency.
Cloud builds consume Expo plan resources. An active paid Apple Developer Program
membership and permission to manage its signing profiles are required for ad hoc
installation on a registered iPhone.

From `mobile`:

```powershell
npx --yes eas-cli@27.1.0 login
npx --yes eas-cli@27.1.0 init
npx --yes eas-cli@27.1.0 device:create
npx --yes eas-cli@27.1.0 device:list

npx --yes eas-cli@27.1.0 env:create --environment development --name EXPO_PUBLIC_API_URL --value https://your-existing-agenti-host/api --visibility plaintext
npx --yes eas-cli@27.1.0 env:create --environment preview --name EXPO_PUBLIC_API_URL --value https://your-existing-agenti-host/api --visibility plaintext

npm run build:internal
```

`eas init` links the Expo project and records its non-secret project ID in
`app.json`. Select the correct Expo owner and Apple team; the repository does not
invent account or device identifiers. The proposed bundle ID is
`com.eastseat.agenti.mobile`; select an identifier owned by your team before the
first build, then keep it stable. Follow the device-registration link on the
iPhone, approve registration, and include that device when generating the
provisioning profile. Let EAS manage certificates through its interactive
credential flow; never copy credentials into Git.

Open the successful build's install URL in Safari on the registered iPhone.
The `internal` profile embeds JavaScript and does not require Metro. The
`development` profile creates a development client:

```powershell
npm run build:development
npx expo start --dev-client
```

Add newly registered devices through a new interactive build (or EAS re-signing).
An older provisioning profile will not include a newly added UDID. Enable iOS
Developer Mode if requested for the development client.

Restrict **Unauthenticated access to internal builds** in the Expo project
settings. Build URLs and device identifiers must not be posted publicly.
Neither profile submits to any store, and no OTA update pipeline is configured.

## Layout and scope

`src/app` contains routes only. `src/screens` contains screen bodies,
`src/api` contains runtime-validated contracts and the shared HTTP client,
`src/auth` owns sessions, and `src/components`/`src/theme` hold shared UI.
Tests are beside the code they exercise; native `ios` and `android` folders are
generated by EAS and ignored.

The app provides dashboard, scoped agents and session details, own cash-count
draft/save/submit, branch vault activity and authorized manual requests/review,
and session/profile information. It does not implement customer transactions,
bank runs, statement import/reconciliation, cash-count approvals, historical
corrections, user administration, or offline synchronization. Use the web
workspace for those operations. Server rules, not route visibility, authorize
every supported operation.

See [the iOS architecture and operations guide](../docs/IOS_MOBILE_APP.md) for API
contracts, security, troubleshooting, and the physical-iPhone acceptance checklist.
