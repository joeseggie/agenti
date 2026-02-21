# Android App: Play Store Deployment & Developer Guide

## Overview

The Agenti Android app is built with **.NET MAUI** (Multi-platform App UI), targeting **Android 7.0+
(API 24+)**. This guide walks you through creating a Google Play developer account, setting up the
required signing, and publishing the app.

---

## Android Version Compatibility

| Android Version | API Level | Market Share (approx.) | Supported? |
|-----------------|-----------|------------------------|------------|
| 14              | 34        | ~25%                   | ✅          |
| 13              | 33        | ~20%                   | ✅          |
| 12              | 31–32     | ~18%                   | ✅          |
| 11              | 30        | ~15%                   | ✅          |
| 10              | 29        | ~9%                    | ✅          |
| 9.0 (Pie)       | 28        | ~5%                    | ✅          |
| 8.0/8.1 (Oreo)  | 26–27     | ~4%                    | ✅          |
| 7.0/7.1 (Nougat)| 24–25     | ~1%                    | ✅ Minimum  |
| 6.0 (Marshmallow)| 23       | <1%                    | ❌ Too old  |

**The app targets Android 7.0 (API 24) as the minimum**, which covers approximately 97–98% of active
Android devices as of 2024. Users on Android 6.x or older will not be able to install the app from
the Play Store.

### Why API 24 (Android 7.0)?
- Covers the vast majority of phones in active use
- Has native support for background work, proper TLS 1.2/1.3, and modern UI features
- Android 6.0 (API 23) devices are less than 1% of the market
- Google Play Store requires a minimum target SDK of 34 (Android 14) for new app submissions

---

## Step 1: Create a Google Play Developer Account

1. Go to [play.google.com/console](https://play.google.com/console)
2. Sign in with a Google account (create a new business account if possible)
3. Pay the **one-time $25 USD** registration fee
4. Fill in your developer profile (name, address, contact info)
5. Accept the Developer Distribution Agreement
6. Account verification typically takes **24–48 hours**

> **Tip:** Use a dedicated Google account for your business, not a personal account. This makes it
> easier to add team members later.

---

## Step 2: Create a New App in Play Console

1. In the Play Console dashboard, click **Create app**
2. Fill in:
   - **App name:** Agenti
   - **Default language:** English (or your primary language)
   - **App or game:** App
   - **Free or paid:** Select appropriately
3. Accept the declarations
4. Click **Create app**

---

## Step 3: Configure App Signing

Android apps must be signed with a **keystore** (a digital certificate). Google recommends using
**Play App Signing**, where Google manages the final signing key.

### Generate an upload keystore

Run this command on your development machine:

```bash
keytool -genkey -v -keystore agenti-release.jks \
    -keyalg RSA -keysize 2048 -validity 10000 \
    -alias agenti-key
```

**Store the keystore file and password securely!** If you lose them, you cannot update the app.

### Configure signing in MAUI

Add to `EastSeat.Agenti.Android/EastSeat.Agenti.Android.csproj` (inside a `Release` PropertyGroup):

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <AndroidKeyStore>true</AndroidKeyStore>
    <AndroidSigningKeyStore>agenti-release.jks</AndroidSigningKeyStore>
    <AndroidSigningKeyAlias>agenti-key</AndroidSigningKeyAlias>
    <AndroidSigningKeyPass>$(ANDROID_SIGNING_KEY_PASS)</AndroidSigningKeyPass>
    <AndroidSigningStorePass>$(ANDROID_SIGNING_STORE_PASS)</AndroidSigningStorePass>
</PropertyGroup>
```

Pass passwords via environment variables to avoid committing secrets.

---

## Step 4: Configure the Backend Server URL

Before building the release APK, update the server URL in `MauiProgram.cs`:

```csharp
builder.Services.AddHttpClient<IApiService, ApiService>(client =>
{
    // Replace with your production server URL
    client.BaseAddress = new Uri("https://your-production-server.example.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

The backend server must:
- Be accessible from the internet (public IP or domain)
- Use **HTTPS** (required by Android)
- Have the `Jwt__Key` environment variable set to a secure random value

---

## Step 5: Configure the JWT Key on the Server

On your production server, set the JWT signing key environment variable:

```bash
# Generate a secure key (32+ characters)
openssl rand -base64 32

# Set it as an environment variable on your server
export Jwt__Key="your-generated-secure-key-here"
export Jwt__Issuer="EastSeat.Agenti"
export Jwt__Audience="EastSeat.Agenti.Android"
export Jwt__ExpiryMinutes="60"
```

For Docker deployments, add to `docker-compose.yml`:

```yaml
environment:
  - Jwt__Key=${JWT_KEY}
  - Jwt__Issuer=EastSeat.Agenti
  - Jwt__Audience=EastSeat.Agenti.Android
  - Jwt__ExpiryMinutes=60
```

---

## Step 6: Build the Release APK/AAB

Google Play requires an **AAB (Android App Bundle)** format (not APK):

```bash
cd EastSeat.Agenti.Android

dotnet publish -f net10.0-android -c Release \
    -p:AndroidPackageFormat=aab \
    -p:AndroidSigningKeyStore=agenti-release.jks \
    -p:AndroidSigningKeyAlias=agenti-key \
    -p:AndroidSigningKeyPass=$ANDROID_SIGNING_KEY_PASS \
    -p:AndroidSigningStorePass=$ANDROID_SIGNING_STORE_PASS
```

The AAB file will be in `bin/Release/net10.0-android/publish/`.

---

## Step 7: Submit to the Play Store

1. In Play Console, go to **Production** → **Create new release**
2. Upload the `.aab` file
3. Enable **Play App Signing** (recommended) - Google manages the release key
4. Fill in the **Release notes** (what's new in this version)
5. Complete the **Store listing** requirements:
   - App title (max 50 chars): `Agenti`
   - Short description (max 80 chars): `Banking agency cash management for agents`
   - Full description (max 4000 chars)
   - Screenshots: Required for phones, tablets optional
   - Feature graphic (1024×500 px)
   - App icon (512×512 px high-res)
6. Complete **Content rating** questionnaire
7. Fill in **Target audience** (COPPA compliance)
8. Complete **Data safety** section (what data the app collects)
9. Click **Review release** → **Start rollout to production**

> **Review time:** New apps typically take **3–7 days** for Google to review. Plan accordingly.

---

## Step 8: Data Safety Requirements

For the Play Store data safety section, the Agenti app:

| Data Type           | Collected? | Shared? | Purpose |
|---------------------|-----------|---------|---------|
| Email address       | Yes       | No      | Authentication |
| Name                | Yes       | No      | Display in app |
| Financial data      | Yes       | No      | Cash count balances |
| Authentication info | Yes       | No      | JWT token (stored in Android Keystore) |

The JWT token is stored in **Android Keystore** (via `SecureStorage`), which provides
hardware-backed security on supported devices (Android 6.0+).

---

## Common Issues

### "App not compatible with device"
- Check that `SupportedOSPlatformVersion` is set to `24.0` in the `.csproj`
- The device must have Android 7.0 or later

### "App crashes on Android 7.x"
- Ensure `cleartext traffic` is disabled in production (HTTPS only)
- Check that TLS 1.2+ is configured on the server

### "Network requests fail"
- Android 9.0+ blocks HTTP (cleartext) traffic by default
- The backend **must** use HTTPS in production
- For local development testing only, add a network security config

### "Slow on older phones"
- Android 7/8 devices may have slower CPUs and less RAM
- Avoid loading large lists all at once (implement pagination)

---

## Security Notes

- **JWT tokens** expire after 60 minutes by default (configurable via `Jwt__ExpiryMinutes`)
- Tokens are stored in **Android Keystore** via `SecureStorage` (encrypted at rest)
- All API communication must use **HTTPS** in production
- The JWT signing key must be a **minimum of 32 characters** and randomly generated
- Never commit the JWT signing key to source control
