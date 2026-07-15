String normalizeBranchSpec(String branch) {
    String value = branch?.trim()
    if (!value) {
        return '*/main'
    }
    value = value.replaceFirst(/^refs\/heads\//, '')
    value = value.replaceFirst(/^origin\//, '')
    if (value.startsWith('*/')) {
        return value
    }
    return "*/${value}"
}

String resolveUnityPath(String overridePath, String unityVersion) {
    String explicitPath = overridePath?.trim()
    if (explicitPath) {
        return explicitPath
    }
    String version = unityVersion?.trim()
    if (!version) {
        version = '6000.5.1f1'
    }
    return "/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/MacOS/Unity"
}

String xmlEscape(String value) {
    return (value ?: '')
        .replace('&', '&amp;')
        .replace('<', '&lt;')
        .replace('>', '&gt;')
        .replace('"', '&quot;')
        .replace("'", '&apos;')
}

String renderExportOptionsPlist(
    String exportMethod,
    boolean automaticSigning,
    String teamId,
    String bundleIdentifier,
    String provisioningProfileSpecifier
) {
    String signingStyle = automaticSigning ? 'automatic' : 'manual'
    String teamBlock = teamId?.trim()
        ? """
    <key>teamID</key>
    <string>${xmlEscape(teamId.trim())}</string>"""
        : ''
    String profileBlock = (!automaticSigning && bundleIdentifier?.trim() && provisioningProfileSpecifier?.trim())
        ? """
    <key>provisioningProfiles</key>
    <dict>
        <key>${xmlEscape(bundleIdentifier.trim())}</key>
        <string>${xmlEscape(provisioningProfileSpecifier.trim())}</string>
    </dict>"""
        : ''

    return """<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>destination</key>
    <string>export</string>
    <key>method</key>
    <string>${xmlEscape(exportMethod?.trim() ?: 'development')}</string>
    <key>signingStyle</key>
    <string>${signingStyle}</string>${teamBlock}${profileBlock}
    <key>stripSwiftSymbols</key>
    <true/>
    <key>manageAppVersionAndBuildNumber</key>
    <false/>
</dict>
</plist>
"""
}

pipeline {
    agent any

    options {
        skipDefaultCheckout(true)
        disableConcurrentBuilds()
        buildDiscarder(logRotator(numToKeepStr: '10'))
        timeout(time: 240, unit: 'MINUTES')
        timestamps()
    }

    parameters {
        string(name: 'GIT_URL', defaultValue: 'https://github.com/bodacheng/FXTrade.git', description: 'FXTrade repository URL.')
        string(name: 'BRANCH', defaultValue: 'main', description: 'Git branch to build. Examples: main, refs/heads/main, */main.')
        string(name: 'GIT_CREDENTIALS_ID', defaultValue: 'bodacheng', description: 'Jenkins credentialsId for the repository. Leave empty for public/anonymous checkout.')

        string(name: 'UNITY_VERSION', defaultValue: '6000.5.1f1', description: 'Unity Hub editor version used by this project.')
        string(name: 'UNITY_PATH_OVERRIDE', defaultValue: '', description: 'Optional absolute Unity executable path. Overrides UNITY_VERSION when set.')

        choice(name: 'XCODE_CONFIGURATION', choices: ['Release', 'Debug', 'ReleaseForProfiling', 'ReleaseForRunning'], description: 'Xcode configuration used for archive.')
        choice(name: 'EXPORT_METHOD', choices: ['development', 'ad-hoc', 'app-store', 'enterprise'], description: 'xcodebuild -exportArchive method.')
        booleanParam(name: 'DEVELOPMENT_BUILD', defaultValue: false, description: 'Build Unity with Development and AllowDebugging options.')

        string(name: 'PRODUCT_NAME', defaultValue: 'TestFXTrade', description: 'Unity PlayerSettings.productName for this build.')
        string(name: 'BUNDLE_IDENTIFIER', defaultValue: 'com.DefaultCompany.TestFXTrade', description: 'iOS bundle identifier.')
        string(name: 'BUNDLE_VERSION', defaultValue: '1.0', description: 'CFBundleShortVersionString.')

        booleanParam(name: 'AUTOMATIC_SIGNING', defaultValue: true, description: 'Use automatic code signing in Unity/Xcode.')
        string(name: 'DEVELOPMENT_TEAM', defaultValue: '', description: 'Apple Developer Team ID. Required for signed device/archive builds.')
        string(name: 'PROVISIONING_PROFILE_SPECIFIER', defaultValue: '', description: 'Manual signing profile name or UUID. Used only when AUTOMATIC_SIGNING is false.')
        choice(name: 'PROVISIONING_PROFILE_TYPE', choices: ['Development', 'Distribution'], description: 'Unity manual provisioning profile type.')
        booleanParam(name: 'ALLOW_PROVISIONING_UPDATES', defaultValue: false, description: 'Pass -allowProvisioningUpdates to xcodebuild.')

        booleanParam(name: 'RUN_EDITMODE_TESTS', defaultValue: false, description: 'Run Unity EditMode tests before the iOS export.')
        booleanParam(name: 'CLEAN_LIBRARY', defaultValue: false, description: 'Delete Unity Library before building.')
        booleanParam(name: 'CLEAN_WORKSPACE', defaultValue: false, description: 'Delete Jenkins workspace before checkout.')

        booleanParam(name: 'INSTALL_POD', defaultValue: true, description: 'Run pod install when the generated Xcode project contains a Podfile.')
        booleanParam(name: 'UNLOCK_KEYCHAIN', defaultValue: true, description: 'Unlock login keychain before xcodebuild.')
        string(name: 'KEYCHAIN_PASSWORD_CREDENTIAL_ID', defaultValue: 'PCUSER_PASSWORD', description: 'Jenkins secret text credentialsId for the macOS keychain password.')
        string(name: 'KEYCHAIN_PATH', defaultValue: '', description: 'Optional keychain path. Defaults to $HOME/Library/Keychains/login.keychain-db.')
    }

    environment {
        BUILD_TARGET = 'iOS'
        UNITY_BUILD_METHOD = 'TestFXTrade.Editor.Build.JenkinsIOSBuild.BuildIOS'
        LOG_DIR = 'Logs/Jenkins'
        XCODE_OUTPUT_PATH = 'build_ios/Export'
        ARCHIVE_PATH = 'build_ios/Archive/FXTrade.xcarchive'
        IPA_OUTPUT_PATH = 'build_ios/Ipa'
        DERIVED_DATA_PATH = 'build_ios/DerivedData'
    }

    stages {
        stage('Clean Workspace') {
            when {
                expression { return params.CLEAN_WORKSPACE }
            }
            steps {
                deleteDir()
            }
        }

        stage('Checkout') {
            steps {
                script {
                    Map remoteConfig = [url: params.GIT_URL.trim()]
                    if (params.GIT_CREDENTIALS_ID?.trim()) {
                        remoteConfig.credentialsId = params.GIT_CREDENTIALS_ID.trim()
                    }

                    checkout([
                        $class: 'GitSCM',
                        branches: [[name: normalizeBranchSpec(params.BRANCH)]],
                        extensions: [
                            [$class: 'CloneOption', timeout: 360],
                            [$class: 'CheckoutOption', timeout: 360]
                        ],
                        userRemoteConfigs: [remoteConfig]
                    ])

                    env.GIT_COMMIT_SHORT = sh(script: 'git rev-parse --short=8 HEAD', returnStdout: true).trim()
                    currentBuild.description = "${params.BRANCH} ${env.GIT_COMMIT_SHORT}"
                }
            }
        }

        stage('Clean Library') {
            when {
                expression { return params.CLEAN_LIBRARY }
            }
            steps {
                dir('Library') {
                    deleteDir()
                }
            }
        }

        stage('Prepare') {
            steps {
                script {
                    env.RESOLVED_UNITY_PATH = resolveUnityPath(params.UNITY_PATH_OVERRIDE, params.UNITY_VERSION)
                }
                sh '''#!/bin/bash
set -euo pipefail

rm -rf "$XCODE_OUTPUT_PATH" "$ARCHIVE_PATH" "$IPA_OUTPUT_PATH" "$DERIVED_DATA_PATH"
mkdir -p "$LOG_DIR" "$XCODE_OUTPUT_PATH" "$(dirname "$ARCHIVE_PATH")" "$IPA_OUTPUT_PATH" "$DERIVED_DATA_PATH"

if [ ! -x "$RESOLVED_UNITY_PATH" ]; then
    echo "Unity executable not found or not executable: $RESOLVED_UNITY_PATH" >&2
    exit 2
fi

xcodebuild -version
'''
            }
        }

        stage('EditMode Tests') {
            when {
                expression { return params.RUN_EDITMODE_TESTS }
            }
            steps {
                sh '''#!/bin/bash
set -euo pipefail

"$RESOLVED_UNITY_PATH" \
  -projectPath "$WORKSPACE" \
  -quit \
  -batchmode \
  -nographics \
  -runTests \
  -testPlatform EditMode \
  -testResults "$WORKSPACE/$LOG_DIR/editmode-tests.xml" \
  -logFile "$WORKSPACE/$LOG_DIR/editmode-tests.log"
'''
            }
        }

        stage('Unity Export Xcode') {
            steps {
                sh '''#!/bin/bash
set -euo pipefail

UNITY_ARGS=(
  -projectPath "$WORKSPACE"
  -quit
  -batchmode
  -nographics
  -executeMethod "$UNITY_BUILD_METHOD"
  -logFile "$WORKSPACE/$LOG_DIR/ios-build-$BUILD_NUMBER.log"
  -buildTarget "$BUILD_TARGET"
  -OutputPath "$WORKSPACE/$XCODE_OUTPUT_PATH"
  -buildNumber "$BUILD_NUMBER"
  -developmentBuild "$DEVELOPMENT_BUILD"
  -automaticSigning "$AUTOMATIC_SIGNING"
  -provisioningProfileType "$PROVISIONING_PROFILE_TYPE"
)

[ -n "${PRODUCT_NAME:-}" ] && UNITY_ARGS+=( -productName "$PRODUCT_NAME" )
[ -n "${BUNDLE_IDENTIFIER:-}" ] && UNITY_ARGS+=( -bundleIdentifier "$BUNDLE_IDENTIFIER" )
[ -n "${BUNDLE_VERSION:-}" ] && UNITY_ARGS+=( -bundleVersion "$BUNDLE_VERSION" )
[ -n "${DEVELOPMENT_TEAM:-}" ] && UNITY_ARGS+=( -appleTeamId "$DEVELOPMENT_TEAM" )
[ -n "${PROVISIONING_PROFILE_SPECIFIER:-}" ] && UNITY_ARGS+=( -provisioningProfileSpecifier "$PROVISIONING_PROFILE_SPECIFIER" )

"$RESOLVED_UNITY_PATH" "${UNITY_ARGS[@]}"
'''
            }
        }

        stage('Pod Install') {
            when {
                expression { return params.INSTALL_POD }
            }
            steps {
                sh '''#!/bin/bash
set -euo pipefail

if [ ! -f "$WORKSPACE/$XCODE_OUTPUT_PATH/Podfile" ]; then
    echo "No Podfile found in $XCODE_OUTPUT_PATH; skipping pod install."
    exit 0
fi

POD_BIN="$(command -v pod || true)"
if [ -z "$POD_BIN" ]; then
    for candidate in "$HOME"/.gem/ruby/*/bin/pod /opt/homebrew/bin/pod /usr/local/bin/pod; do
        if [ -x "$candidate" ]; then
            POD_BIN="$candidate"
            break
        fi
    done
fi

if [ -z "$POD_BIN" ]; then
    echo "pod command not found. Checked PATH, ~/.gem/ruby/*/bin/pod, /opt/homebrew/bin/pod, and /usr/local/bin/pod." >&2
    exit 127
fi

"$POD_BIN" install --project-directory="$WORKSPACE/$XCODE_OUTPUT_PATH"
'''
            }
        }

        stage('Unlock Keychain') {
            when {
                expression { return params.UNLOCK_KEYCHAIN }
            }
            steps {
                script {
                    if (!params.KEYCHAIN_PASSWORD_CREDENTIAL_ID?.trim()) {
                        error('KEYCHAIN_PASSWORD_CREDENTIAL_ID is required when UNLOCK_KEYCHAIN is true.')
                    }
                }
                withCredentials([string(credentialsId: params.KEYCHAIN_PASSWORD_CREDENTIAL_ID.trim(), variable: 'KEYCHAIN_PASSWORD')]) {
                    sh '''#!/bin/bash
set -euo pipefail

KEYCHAIN="$KEYCHAIN_PATH"
if [ -z "$KEYCHAIN" ]; then
    KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"
fi

security unlock-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN"
security set-keychain-settings -lut 21600 "$KEYCHAIN"
'''
                }
            }
        }

        stage('Xcode Archive') {
            steps {
                sh '''#!/bin/bash
set -euo pipefail

cd "$WORKSPACE/$XCODE_OUTPUT_PATH"

if [ -d "Unity-iPhone.xcworkspace" ]; then
    CONTAINER_ARGS=( -workspace "Unity-iPhone.xcworkspace" )
elif [ -d "Unity-iPhone.xcodeproj" ]; then
    CONTAINER_ARGS=( -project "Unity-iPhone.xcodeproj" )
else
    echo "Unity-iPhone.xcodeproj/xcworkspace was not generated in $WORKSPACE/$XCODE_OUTPUT_PATH" >&2
    exit 3
fi

SIGNING_ARGS=()
if [ "$AUTOMATIC_SIGNING" = "true" ]; then
    SIGNING_ARGS+=( CODE_SIGN_STYLE=Automatic )
else
    SIGNING_ARGS+=( CODE_SIGN_STYLE=Manual )
fi

[ -n "${DEVELOPMENT_TEAM:-}" ] && SIGNING_ARGS+=( DEVELOPMENT_TEAM="$DEVELOPMENT_TEAM" )
[ -n "${BUNDLE_IDENTIFIER:-}" ] && SIGNING_ARGS+=( PRODUCT_BUNDLE_IDENTIFIER="$BUNDLE_IDENTIFIER" )
[ -n "${PROVISIONING_PROFILE_SPECIFIER:-}" ] && SIGNING_ARGS+=( PROVISIONING_PROFILE_SPECIFIER="$PROVISIONING_PROFILE_SPECIFIER" )

PROVISIONING_UPDATE_ARGS=()
if [ "$ALLOW_PROVISIONING_UPDATES" = "true" ]; then
    PROVISIONING_UPDATE_ARGS+=( -allowProvisioningUpdates )
fi

xcodebuild "${CONTAINER_ARGS[@]}" \
  "${PROVISIONING_UPDATE_ARGS[@]}" \
  -scheme Unity-iPhone \
  -configuration "$XCODE_CONFIGURATION" \
  -destination 'generic/platform=iOS' \
  -archivePath "$WORKSPACE/$ARCHIVE_PATH" \
  -derivedDataPath "$WORKSPACE/$DERIVED_DATA_PATH" \
  clean archive \
  "${SIGNING_ARGS[@]}"
'''
            }
        }

        stage('Export IPA') {
            steps {
                script {
                    if (!params.AUTOMATIC_SIGNING && !params.PROVISIONING_PROFILE_SPECIFIER?.trim()) {
                        error('PROVISIONING_PROFILE_SPECIFIER is required when AUTOMATIC_SIGNING is false.')
                    }

                    writeFile(
                        file: "${env.XCODE_OUTPUT_PATH}/ExportOptions.plist",
                        text: renderExportOptionsPlist(
                            params.EXPORT_METHOD,
                            params.AUTOMATIC_SIGNING,
                            params.DEVELOPMENT_TEAM,
                            params.BUNDLE_IDENTIFIER,
                            params.PROVISIONING_PROFILE_SPECIFIER
                        )
                    )
                }
                sh '''#!/bin/bash
set -euo pipefail

PROVISIONING_UPDATE_ARGS=()
if [ "$ALLOW_PROVISIONING_UPDATES" = "true" ]; then
    PROVISIONING_UPDATE_ARGS+=( -allowProvisioningUpdates )
fi

xcodebuild -exportArchive \
  "${PROVISIONING_UPDATE_ARGS[@]}" \
  -archivePath "$WORKSPACE/$ARCHIVE_PATH" \
  -exportPath "$WORKSPACE/$IPA_OUTPUT_PATH" \
  -exportOptionsPlist "$WORKSPACE/$XCODE_OUTPUT_PATH/ExportOptions.plist"

find "$WORKSPACE/$IPA_OUTPUT_PATH" -maxdepth 1 -name '*.ipa' -print
'''
            }
        }
    }

    post {
        always {
            archiveArtifacts(
                allowEmptyArchive: true,
                artifacts: 'build_ios/Ipa/*.ipa, build_ios/Export/ExportOptions.plist, Logs/Jenkins/*.log, Logs/Jenkins/*.xml',
                fingerprint: true,
                followSymlinks: false
            )
        }
    }
}
