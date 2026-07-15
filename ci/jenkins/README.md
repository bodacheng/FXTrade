# FXTrade iOS Jenkins Pipeline

The local Jenkins job `FXTradeIOSBuild` builds and uploads the Unity iOS app to App Store Connect. Its approved inline Groovy mirrors the repository `Jenkinsfile`.

## Fixed project settings

- Unity version: read from `ProjectSettings/ProjectVersion.txt`
- Product: `TestFXTrade`
- Bundle identifier: `com.BO.TestFXTrade`
- Apple team: `S88E744TXJ`
- Signing: automatic, with provisioning updates enabled
- Export method: `app-store-connect`
- Git credential: `bodacheng`
- Keychain credential: `PCUSER_PASSWORD`
- App Store credentials: `AppleStore_API_Key` and `AppleStore_API_Issuer`

The App Store Connect `.p8` used by `CustomIOSBuild` is installed once at `~/.appstoreconnect/private_keys/AuthKey_<KEY_ID>.p8`. It is not copied into this repository or the FXTrade Jenkins workspace.

## Build parameters

- `BRANCH`: Git branch, default `main`
- `APP_VERSION`: `CFBundleShortVersionString`, default `1.0`
- `CLEAN_LIBRARY`: delete the Unity `Library` cache
- `CLEAN_WORKSPACE`: delete the Jenkins workspace before checkout
- `UPLOAD_APP_STORE`: validate and upload after exporting the IPA

There is no Asset Build or CocoaPods stage. The repository currently has no asset-build dependency or `Podfile`.

## Pipeline

1. Checkout the requested branch.
2. Read and verify the required Unity editor version.
3. Export the iOS Xcode project through `JenkinsIOSBuild.BuildIOS`.
4. Unlock the login keychain.
5. Archive with automatic signing.
6. Export and archive the IPA.
7. Validate and upload the IPA to App Store Connect.

Jenkins keeps the IPA, the current Unity build log, and `ExportOptions.plist` as build artifacts.
