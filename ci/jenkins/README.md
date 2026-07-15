# FXTrade iOS Jenkins Pipeline

This project contains a self-contained `Jenkinsfile` for exporting a Unity iOS Xcode project, archiving it with Xcode, and exporting an `.ipa`.

## Jenkins Job Setup

Create a Pipeline job and point it at this repository:

- Repository: `https://github.com/bodacheng/FXTrade.git`
- Branch: `main`
- Script path: `Jenkinsfile`
- Recommended macOS agent requirements:
  - Unity `6000.5.1f1` installed by Unity Hub
  - Xcode command line tools installed and selected
  - Apple signing certificate and provisioning profile installed in the build user's keychain
  - `PCUSER_PASSWORD` Jenkins secret text credential, or change `KEYCHAIN_PASSWORD_CREDENTIAL_ID`

The pipeline is modeled after the local Jenkins `CustomIOSBuild` job:

1. Checkout the selected branch.
2. Optionally delete `Library`.
3. Optionally run Unity EditMode tests.
4. Run Unity in batchmode with `TestFXTrade.Editor.Build.JenkinsIOSBuild.BuildIOS`.
5. Run `pod install` only when the exported Xcode project contains a `Podfile`.
6. Unlock the macOS keychain.
7. Run `xcodebuild archive`.
8. Run `xcodebuild -exportArchive`.
9. Archive the `.ipa`, Unity logs, and generated `ExportOptions.plist`.

## Important Parameters

- `UNITY_VERSION`: defaults to `6000.5.1f1`.
- `BUNDLE_IDENTIFIER`: defaults to the app identifier `com.BO.TestFxTrade`.
- `DEVELOPMENT_TEAM`: Apple Developer Team ID.
- `AUTOMATIC_SIGNING`: use automatic signing when true. For manual signing, set `PROVISIONING_PROFILE_SPECIFIER`.
- `EXPORT_METHOD`: `development`, `ad-hoc`, `app-store`, or `enterprise`.
- `ALLOW_PROVISIONING_UPDATES`: enable only if the Jenkins agent is allowed to let Xcode manage signing.

The pipeline does not upload to App Store Connect. It only produces an `.ipa` artifact under `build_ios/Ipa`.
