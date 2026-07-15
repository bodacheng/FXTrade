String normalizeBranchSpec(String branch)
{
    String value = branch?.trim()
    if (!value)
    {
        return 'main'
    }

    return value.replace('refs/heads/', '').replace('origin/', '').replace('*/', '')
}

String exportOptionsPlist()
{
    return '''<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>destination</key>
    <string>export</string>
    <key>method</key>
    <string>app-store-connect</string>
    <key>signingStyle</key>
    <string>automatic</string>
    <key>teamID</key>
    <string>S88E744TXJ</string>
    <key>stripSwiftSymbols</key>
    <true/>
    <key>manageAppVersionAndBuildNumber</key>
    <false/>
</dict>
</plist>
'''
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
        string(name: 'BRANCH', defaultValue: 'main', description: 'Git branch to build.')
        string(name: 'APP_VERSION', defaultValue: '1.0', description: 'CFBundleShortVersionString.')
        booleanParam(name: 'CLEAN_LIBRARY', defaultValue: false, description: 'Delete the Unity Library cache before building.')
        booleanParam(name: 'CLEAN_WORKSPACE', defaultValue: false, description: 'Delete the Jenkins workspace before checkout.')
        booleanParam(name: 'UPLOAD_APP_STORE', defaultValue: true, description: 'Validate and upload the IPA to App Store Connect.')
    }

    environment {
        GIT_URL = 'https://github.com/bodacheng/FXTrade.git'
        GIT_CREDENTIALS_ID = 'bodacheng'
        UNITY_BUILD_METHOD = 'TestFXTrade.Editor.Build.JenkinsIOSBuild.BuildIOS'
        APPLE_TEAM_ID = 'S88E744TXJ'
        APP_BUNDLE_ID = 'com.BO.TestFXTrade'
        XCODE_OUTPUT_PATH = 'build_ios/Export'
        ARCHIVE_PATH = 'build_ios/Archive/FXTrade.xcarchive'
        IPA_OUTPUT_PATH = 'build_ios/Ipa'
        DERIVED_DATA_PATH = 'build_ios/DerivedData'
        EXPORT_OPTIONS_PATH = 'build_ios/Export/ExportOptions.plist'
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
                    String branch = normalizeBranchSpec(params.BRANCH?.toString())
                    checkout([
                        $class: 'GitSCM',
                        branches: [[name: branch]],
                        extensions: [
                            [$class: 'CloneOption', timeout: 360],
                            [$class: 'CheckoutOption', timeout: 360]
                        ],
                        userRemoteConfigs: [[
                            credentialsId: env.GIT_CREDENTIALS_ID,
                            url: env.GIT_URL
                        ]]
                    ])

                    String gitHash = sh(script: 'git rev-parse --short=8 HEAD', returnStdout: true).trim()
                    currentBuild.description = "Version: ${params.APP_VERSION}\nBranch: ${branch}\nGit: ${gitHash}"
                }
            }
        }

        stage('Prepare') {
            steps {
                sh '''#!/bin/bash
set -euo pipefail

if [[ ! "$APP_VERSION" =~ ^[0-9]+([.][0-9]+){0,2}$ ]]; then
    echo "APP_VERSION must contain one to three numeric components: $APP_VERSION" >&2
    exit 2
fi

if [ "$CLEAN_LIBRARY" = "true" ]; then
    rm -rf Library
fi

rm -rf "$XCODE_OUTPUT_PATH" "$ARCHIVE_PATH" "$IPA_OUTPUT_PATH" "$DERIVED_DATA_PATH"
mkdir -p Logs/Jenkins "$XCODE_OUTPUT_PATH" "$(dirname "$ARCHIVE_PATH")" "$IPA_OUTPUT_PATH" "$DERIVED_DATA_PATH"

UNITY_VERSION="$(awk '$1 == "m_EditorVersion:" { print $2; exit }' ProjectSettings/ProjectVersion.txt)"
UNITY_EXECUTABLE="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
if [ -z "$UNITY_VERSION" ] || [ ! -x "$UNITY_EXECUTABLE" ]; then
    echo "Unity executable not found for ProjectSettings/ProjectVersion.txt: $UNITY_EXECUTABLE" >&2
    exit 3
fi

echo "Unity $UNITY_VERSION"
xcodebuild -version
'''
            }
        }

        stage('Unity Export') {
            steps {
                sh '''#!/bin/bash
set -euo pipefail

UNITY_VERSION="$(awk '$1 == "m_EditorVersion:" { print $2; exit }' ProjectSettings/ProjectVersion.txt)"
UNITY_EXECUTABLE="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"

"$UNITY_EXECUTABLE" \
  -projectPath "$WORKSPACE" \
  -quit \
  -batchmode \
  -nographics \
  -executeMethod "$UNITY_BUILD_METHOD" \
  -logFile "$WORKSPACE/Logs/Jenkins/build_${BUILD_NUMBER}_log.txt" \
  -buildTarget iOS \
  -OutputPath "$XCODE_OUTPUT_PATH" \
  -buildNumber "$BUILD_NUMBER" \
  -bundleVersion "$APP_VERSION" \
  -productName TestFXTrade \
  -bundleIdentifier "$APP_BUNDLE_ID" \
  -appleTeamId "$APPLE_TEAM_ID" \
  -automaticSigning true \
  -provisioningProfileType Distribution
'''
            }
        }

        stage('Unlock Keychain') {
            steps {
                withCredentials([string(credentialsId: 'PCUSER_PASSWORD', variable: 'KEYCHAIN_PASSWORD')]) {
                    sh '''#!/bin/bash
set -euo pipefail

KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"
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

cd "$XCODE_OUTPUT_PATH"
if [ -d "Unity-iPhone.xcworkspace" ]; then
    CONTAINER_ARGS=( -workspace "Unity-iPhone.xcworkspace" )
elif [ -d "Unity-iPhone.xcodeproj" ]; then
    CONTAINER_ARGS=( -project "Unity-iPhone.xcodeproj" )
else
    echo "Unity-iPhone.xcodeproj/xcworkspace was not generated in $XCODE_OUTPUT_PATH" >&2
    exit 4
fi

xcodebuild "${CONTAINER_ARGS[@]}" \
  -allowProvisioningUpdates \
  -scheme Unity-iPhone \
  -configuration Release \
  -destination 'generic/platform=iOS' \
  -archivePath "$WORKSPACE/$ARCHIVE_PATH" \
  -derivedDataPath "$WORKSPACE/$DERIVED_DATA_PATH" \
  archive \
  CODE_SIGN_STYLE=Automatic \
  DEVELOPMENT_TEAM="$APPLE_TEAM_ID"
'''
            }
        }

        stage('Export IPA') {
            steps {
                script {
                    writeFile(file: env.EXPORT_OPTIONS_PATH, text: exportOptionsPlist())
                }
                sh '''#!/bin/bash
set -euo pipefail

xcodebuild -exportArchive \
  -allowProvisioningUpdates \
  -archivePath "$WORKSPACE/$ARCHIVE_PATH" \
  -exportPath "$WORKSPACE/$IPA_OUTPUT_PATH" \
  -exportOptionsPlist "$WORKSPACE/$EXPORT_OPTIONS_PATH"

IPA_FILE="$(find "$WORKSPACE/$IPA_OUTPUT_PATH" -maxdepth 1 -type f -name '*.ipa' -print -quit)"
if [ -z "$IPA_FILE" ]; then
    echo "IPA was not generated under $WORKSPACE/$IPA_OUTPUT_PATH" >&2
    exit 5
fi
'''
            }
        }

        stage('Archive IPA') {
            steps {
                archiveArtifacts(
                    artifacts: 'build_ios/Ipa/*.ipa',
                    fingerprint: true,
                    followSymlinks: false
                )
            }
        }

        stage('Upload App Store') {
            when {
                expression { return params.UPLOAD_APP_STORE }
            }
            steps {
                withCredentials([
                    string(credentialsId: 'AppleStore_API_Key', variable: 'API_KEY'),
                    string(credentialsId: 'AppleStore_API_Issuer', variable: 'API_ISSUER')
                ]) {
                    sh '''#!/bin/bash
set -euo pipefail

PRIVATE_KEY="$HOME/.appstoreconnect/private_keys/AuthKey_${API_KEY}.p8"
if [ ! -f "$PRIVATE_KEY" ]; then
    echo "The shared App Store Connect private key is not installed at ~/.appstoreconnect/private_keys." >&2
    exit 6
fi

IPA_FILE="$(find "$WORKSPACE/$IPA_OUTPUT_PATH" -maxdepth 1 -type f -name '*.ipa' -print -quit)"
xcrun altool --validate-app -f "$IPA_FILE" -t ios --apiKey "$API_KEY" --apiIssuer "$API_ISSUER" --verbose
xcrun altool --upload-app -f "$IPA_FILE" -t ios --apiKey "$API_KEY" --apiIssuer "$API_ISSUER" --verbose
'''
                }
            }
        }
    }

    post {
        always {
            archiveArtifacts(
                allowEmptyArchive: true,
                artifacts: "Logs/Jenkins/build_${env.BUILD_NUMBER}_log.txt, build_ios/Export/ExportOptions.plist",
                fingerprint: true,
                followSymlinks: false
            )
        }
    }
}
