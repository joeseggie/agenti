# iOS App: App Store Deployment & Developer Guide

## Overview

The Agenti iOS app is built with **.NET MAUI** (Multi-platform App UI), targeting **iOS 16.1+**.
It connects to the same REST API backend as the Android app and provides full feature parity with
the web application, including:

- Secure login with JWT authentication (stored in iOS Keychain)
- Dashboard with wallet balances and session status
- Cash count (opening and closing) workflow
- Cash session history
- Agent list with search
- User profile

---

## iOS Version Compatibility

| iOS Version | Market Share (approx.) | Supported? |
|-------------|------------------------|------------|
| iOS 18      | ~40%                   | ✅          |
| iOS 17      | ~30%                   | ✅          |
| iOS 16      | ~15%                   | ✅ Minimum  |
| iOS 15      | ~8%                    | ❌ Too old  |
| iOS 14 and older | <5%               | ❌ Too old  |

**The app targets iOS 16.1 as the minimum**, which covers approximately 95%+ of active
iOS devices as of 2024. Users on iOS 15 or older will not be able to install the app.

### Why iOS 16.1?

- Covers the vast majority of iPhones in active use
- Has full support for SwiftUI-equivalent MAUI layouts and modern APIs
- Apple requires a minimum deployment target of iOS 16 for new App Store submissions (as of 2024)
- iOS 15.x devices represent less than 8% of the market

---

## Prerequisites

Before building or running the iOS app, you need:

1. **macOS** — iOS apps can only be built on macOS (Xcode requirement)
2. **Xcode 16+** — Install from the Mac App Store
3. **.NET 10 SDK** — Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download)
4. **.NET MAUI workload** — Run `dotnet workload install maui-ios`
5. **Apple Developer Account** — Required to run on physical devices and publish to the App Store

### Install .NET and MAUI

```bash
# Install .NET 10 SDK (if not already installed)
brew install --cask dotnet-sdk

# Install the iOS/MAUI workload
dotnet workload install maui-ios

# Verify installation
dotnet --version        # Should show 10.x.x
dotnet workload list    # Should show maui-ios
```

---

## Step 1: Set Up a Development Apple Developer Account

### Free Account (Simulator + Ad-hoc Device Testing)

1. Open Xcode → **Preferences** → **Accounts**
2. Sign in with your Apple ID
3. This allows you to run the app on the **iOS Simulator** and up to **3 personal devices** for
   7 days per certificate

### Paid Apple Developer Account ($99/year — Required for App Store)

1. Go to [developer.apple.com/programs](https://developer.apple.com/programs)
2. Enroll as an **Individual** or **Organization**
3. Pay the **$99 USD/year** fee
4. Wait for **24–48 hours** for account approval

> **Tip:** For enterprise/business deployments, consider the
> [Apple Developer Enterprise Program](https://developer.apple.com/programs/enterprise/) ($299/year),
> which allows in-house distribution without the App Store.

---

## Step 2: Register Your App ID (Bundle Identifier)

1. Log in to [developer.apple.com](https://developer.apple.com)
2. Go to **Certificates, Identifiers & Profiles** → **Identifiers**
3. Click **+** to register a new App ID
4. Choose **App IDs** → **App**
5. Fill in:
   - **Description:** Agenti
   - **Bundle ID:** `com.eastseat.agenti` (Explicit)
6. Under **Capabilities**, enable:
   - **Keychain Sharing** (for secure JWT storage)
   - **Push Notifications** (optional, for future features)
7. Click **Register**

---

## Step 3: Install and Test on a Dev iPhone (Sideloading)

This section explains how to run the app on a physical iPhone without going through the App Store.

### 3.1 Connect Your iPhone to Your Mac

1. Connect your iPhone via USB
2. On the iPhone, tap **Trust** when prompted
3. Unlock your iPhone and leave it unlocked during deployment

### 3.2 Register Your Device in Xcode

1. Open **Xcode**
2. Go to **Window** → **Devices and Simulators**
3. Your iPhone should appear in the left sidebar
4. Note the **Device UDID** — you'll need this for provisioning

Alternatively, find the UDID with:
```bash
xcrun xctrace list devices
```

### 3.3 Create a Development Provisioning Profile

**If using a free Apple ID** (no Developer Program):
- Xcode automatically manages provisioning when you build from Xcode
- Certificates last **7 days** and must be renewed

**If using a paid Developer Account:**
1. In [developer.apple.com](https://developer.apple.com) → **Profiles** → click **+**
2. Choose **iOS App Development**
3. Select your **App ID** (`com.eastseat.agenti`)
4. Select your **development certificate**
5. Add your **device UDID**
6. Name it: `Agenti Development`
7. Download and double-click the `.mobileprovision` file to install it

### 3.4 Configure Signing in the Project

Open `EastSeat.Agenti.iOS/EastSeat.Agenti.iOS.csproj` and add your signing configuration:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <CodesignKey>iPhone Developer: Your Name (XXXXXXXXXX)</CodesignKey>
    <CodesignProvision>Agenti Development</CodesignProvision>
</PropertyGroup>
```

Find your signing identity by running:
```bash
security find-identity -v -p codesigning
```

### 3.5 Configure the Development Server URL

For local development, update `Resources/Raw/appsettings.Development.json`
(create this file if it doesn't exist):

```json
{
  "ApiSettings": {
    "BaseUrl": "http://YOUR_MAC_IP_ADDRESS:5113/"
  }
}
```

Find your Mac's IP address:
```bash
ipconfig getifaddr en0
```

> **Important:** Your iPhone must be on the same Wi-Fi network as your Mac.
> If using HTTPS with a self-signed certificate in development, the `DEBUG` build
> configuration already bypasses certificate validation (see `MauiProgram.cs`).

### 3.6 Build and Deploy to iPhone

```bash
cd EastSeat.Agenti.iOS

# Deploy to a connected physical device
dotnet build -f net10.0-ios -c Debug \
    -p:RuntimeIdentifier=ios-arm64 \
    -p:ArchiveOnBuild=false

# OR use the dotnet run command to deploy directly
dotnet run -f net10.0-ios -c Debug
```

Or from Visual Studio for Mac / Rider, select your iPhone as the target device and press **Run**.

### 3.7 Trust the Developer Certificate on iPhone

The first time you run the app on your iPhone:

1. Go to iPhone **Settings** → **General** → **VPN & Device Management**
2. Under **Developer App**, find your Apple ID
3. Tap **Trust "your.apple.id@example.com"**
4. Tap **Trust** again to confirm

The app will now open without an "Untrusted Developer" error.

---

## Step 4: Run on the iOS Simulator

You do **not** need a physical device or Developer Account for the iOS Simulator.

```bash
# List available simulators
xcrun simctl list devices

# Build and run on the iOS Simulator (e.g., iPhone 16 Pro)
cd EastSeat.Agenti.iOS
dotnet run -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-x64

# Or for Apple Silicon Macs:
dotnet run -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64
```

### Using the iOS Simulator with the Local Backend

When using the iOS Simulator, your Mac's `localhost` is accessible directly from the simulator.
Update `Resources/Raw/appsettings.Development.json`:

```json
{
  "ApiSettings": {
    "BaseUrl": "https://localhost:7001/"
  }
}
```

The DEBUG build already accepts self-signed certificates, so the local dev HTTPS certificate works.

---

## Step 5: Configure the Backend Server URL

Before building the release IPA, update the server URL in `Resources/Raw/appsettings.json`:

```json
{
  "ApiSettings": {
    "BaseUrl": "https://your-production-server.example.com/"
  }
}
```

The backend server must:
- Be accessible from the internet (public IP or domain name)
- Use **HTTPS** (required by iOS App Transport Security)
- Have the `Jwt__Key` environment variable set to a secure random value

---

## Step 6: Configure the JWT Key on the Server

On your production server, set the JWT signing key environment variable:

```bash
# Generate a secure key (32+ characters)
openssl rand -base64 32

# Set environment variables on your server
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

> **Note:** The iOS app uses the same JWT audience (`EastSeat.Agenti.Android`) as the Android app,
> since they both consume the same REST API.

---

## Step 7: Build the Release IPA for App Store

### 7.1 Create a Distribution Certificate

1. In [developer.apple.com](https://developer.apple.com) → **Certificates** → click **+**
2. Choose **Apple Distribution** (for App Store)
3. Follow the Certificate Signing Request (CSR) instructions
4. Download and double-click the certificate to install it in your Keychain

### 7.2 Create a Distribution Provisioning Profile

1. In [developer.apple.com](https://developer.apple.com) → **Profiles** → click **+**
2. Choose **App Store Connect**
3. Select your **App ID** (`com.eastseat.agenti`)
4. Select your **distribution certificate**
5. Name it: `Agenti App Store`
6. Download and double-click the `.mobileprovision` file

### 7.3 Build the Release IPA

```bash
cd EastSeat.Agenti.iOS

dotnet publish -f net10.0-ios -c Release \
    -p:RuntimeIdentifier=ios-arm64 \
    -p:ArchiveOnBuild=true \
    -p:CodesignKey="Apple Distribution: Your Company (XXXXXXXXXX)" \
    -p:CodesignProvision="Agenti App Store"
```

The IPA file will be in `bin/Release/net10.0-ios/publish/`.

---

## Step 8: Submit to the App Store

### 8.1 Create an App in App Store Connect

1. Log in to [appstoreconnect.apple.com](https://appstoreconnect.apple.com)
2. Go to **Apps** → click **+** → **New App**
3. Fill in:
   - **Platform:** iOS
   - **Name:** Agenti
   - **Primary Language:** English
   - **Bundle ID:** `com.eastseat.agenti`
   - **SKU:** `agenti-ios-1` (any unique identifier)
4. Click **Create**

### 8.2 Upload the IPA

Use **Transporter** (Mac App Store, free) or the `altool` command:

```bash
# Using xcrun altool (included with Xcode)
xcrun altool --upload-app \
    --type ios \
    --file "bin/Release/net10.0-ios/publish/EastSeat.Agenti.iOS.ipa" \
    --apiKey YOUR_API_KEY \
    --apiIssuer YOUR_ISSUER_ID
```

Or use [Transporter app](https://apps.apple.com/us/app/transporter/id1450874784) (drag & drop).

### 8.3 Complete the App Store Listing

In App Store Connect, fill in:

1. **Version Information:**
   - Version number: `1.0.0`
   - What's New: First release

2. **App Information:**
   - Category: **Business** (or Finance)
   - Content Rights: Confirm you own or have rights to all content

3. **Pricing and Availability:**
   - Select price (Free or Paid)
   - Select available territories

4. **App Review Information:**
   - Demo account credentials (required for review if app requires login)
   - Contact information

5. **App Store Screenshots** (required):
   - 6.9" (iPhone 16 Pro Max): minimum 1 screenshot
   - 6.5" (iPhone 11 Pro Max): optional but recommended
   - 12.9" iPad Pro: required if app supports iPad

6. **Privacy Policy URL** (required)

7. **App Privacy / Data Safety:**

| Data Type           | Collected? | Linked to User? | Purpose |
|---------------------|-----------|-----------------|---------|
| Email address       | Yes       | Yes             | Authentication |
| Name                | Yes       | Yes             | Display in app |
| Financial data      | Yes       | Yes             | Cash count balances |
| Authentication info | Yes       | Yes             | JWT token (Keychain) |

### 8.4 Submit for Review

1. Click **Add for Review**
2. Select your build
3. Answer export compliance questions (No encryption beyond HTTPS standard)
4. Click **Submit to App Review**

> **Review time:** New apps typically take **24–48 hours** for Apple to review.
> This is faster than the Google Play Store.

---

## Step 9: TestFlight (Beta Testing)

TestFlight allows you to distribute the app to up to 10,000 testers before the App Store release.

### Internal Testing (up to 25 testers, no review required)

1. In App Store Connect → **TestFlight** → **Internal Testing**
2. Add testers by their Apple ID email
3. They receive an invitation email and install via the TestFlight app

### External Testing (up to 10,000 testers, requires Apple review)

1. In App Store Connect → **TestFlight** → **External Testing** → click **+**
2. Add a group name and invite testers
3. Submit the build for **Beta App Review** (usually 24–48 hours)

---

## Common Issues

### "Provisioning profile doesn't include the selected device"
- Register your device UDID in [developer.apple.com](https://developer.apple.com) → Devices
- Regenerate the provisioning profile to include the new device

### "The certificate has expired"
- Development certificates expire after **1 year**
- Renew in Xcode → Preferences → Accounts → Manage Certificates

### "App crashes on launch"
- Check `Console.app` on Mac with your iPhone connected for crash logs
- Or use `xcrun devicectl device log collect --device YOUR_DEVICE_ID`

### "Network requests fail (ATS error)"
- iOS App Transport Security (ATS) requires HTTPS for all network requests in production
- The `Info.plist` only allows `NSAllowsLocalNetworking` for local development
- In production, the server **must** use HTTPS with a valid certificate

### "SecureStorage fails on iOS Simulator"
- `SecureStorage` uses the iOS Keychain, which may not be available in some simulator configurations
- Test Keychain-dependent features on a real device
- In the simulator, `SecureStorage` falls back to unencrypted storage

### "Build fails: 'No valid provisioning profiles found'"
- Open the project in Xcode to let Xcode manage provisioning automatically
- Or manually download and install the provisioning profile

### "Slow simulator performance"
- Enable **Metal** in the simulator for better graphics performance
- Use a physical device for the best real-world performance testing

---

## Security Notes

- **JWT tokens** expire after 60 minutes by default (configurable via `Jwt__ExpiryMinutes`)
- Tokens are stored in the **iOS Keychain** via `SecureStorage` (encrypted at rest, hardware-backed
  on devices with Secure Enclave)
- All API communication must use **HTTPS** in production (enforced by iOS App Transport Security)
- The JWT signing key must be a **minimum of 32 characters** and randomly generated
- Never commit the JWT signing key to source control
- The `DEBUG` build configuration bypasses TLS certificate validation — this is **only** for
  development against the local dev server and is stripped from Release builds

---

## Development Workflow

```bash
# 1. Clone the repository
git clone https://github.com/joeseggie/agenti.git
cd agenti

# 2. Install workloads
dotnet workload install maui-ios

# 3. Create development settings for local backend
cat > EastSeat.Agenti.iOS/Resources/Raw/appsettings.Development.json << 'EOF'
{
  "ApiSettings": {
    "BaseUrl": "https://localhost:7001/"
  }
}
EOF

# 4. Start the backend (in a separate terminal)
cd EastSeat.Agenti.Web && dotnet run

# 5. Run on iOS Simulator (Apple Silicon Mac)
cd EastSeat.Agenti.iOS
dotnet run -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64

# 6. Run on iOS Simulator (Intel Mac)
dotnet run -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-x64

# 7. Deploy to physical iPhone (connected via USB)
dotnet run -f net10.0-ios -c Debug -p:RuntimeIdentifier=ios-arm64
```

---

## Additional Resources

- [.NET MAUI iOS documentation](https://learn.microsoft.com/en-us/dotnet/maui/ios/)
- [Apple Developer Documentation](https://developer.apple.com/documentation/)
- [App Store Connect Help](https://developer.apple.com/help/app-store-connect/)
- [TestFlight Documentation](https://developer.apple.com/testflight/)
- [Agenti Android Deployment Guide](ANDROID_DEPLOYMENT.md) — companion guide for the Android app
