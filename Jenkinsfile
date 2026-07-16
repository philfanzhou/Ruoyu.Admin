// Ruoyu.Study - Admin Portal Pipeline
//
// Unified api+frontend pipeline for admin_portal:
//   1. Preflight — verify docker + dotnet + repo access
//   2. Build    — reuse script/build-script/10-admin-portal.build.sh (docker build)
//   3. UT       — run unit tests directly on host via dotnet SDK 8.0
//   4. Deploy   — restart the ruoyu-admin container via start.sh
//   5. Smoke    — root check on localhost:10901/

pipeline {
    agent any

    options {
        timestamps()
        timeout(time: 30, unit: 'MINUTES')
        buildDiscarder(logRotator(numToKeepStr: '20'))
        disableConcurrentBuilds()
    }

    environment {
        REPO_DIR         = '/mnt/data1/Ruoyu.Study'
        SERVICE_DIR      = "${env.REPO_DIR}/src/admin_portal"
        BUILD_SCRIPT     = "${env.REPO_DIR}/script/build-script/10-admin-portal.build.sh"
        TEST_PROJ        = "${env.SERVICE_DIR}/backend/Tests/Admin.WebApi.Tests.csproj"
        START_SCRIPT     = "${env.SERVICE_DIR}/start.sh"
        REPORT_DIR       = "${env.WORKSPACE}/reports"
        NUGET_SOURCE     = 'https://repo.huaweicloud.com/repository/nuget/v3/index.json'
    }

    triggers { pollSCM('H/5 * * * *') }

    stages {
        stage('Preflight') {
            steps {
                sh '''
                    set -e
                    echo "=== Jenkins user ==="
                    id
                    echo ""
                    echo "=== Docker access ==="
                    docker info --format 'Server Version: {{.ServerVersion}}'
                    echo ""
                    echo "=== dotnet SDK ==="
                    dotnet --version
                    echo ""
                    echo "=== Repo symlink ==="
                    ls -la "$REPO_DIR" | head -10
                    echo ""
                    echo "=== Admin Portal source tree ==="
                    ls -la "$SERVICE_DIR"
                    echo ""
                    echo "=== Build script ==="
                    ls -la "$BUILD_SCRIPT"
                    echo ""
                    echo "=== Test project ==="
                    ls -la "$TEST_PROJ"
                '''
            }
        }

        stage('Build Image') {
            steps {
                sh '''
                    set -e
                    cd "$REPO_DIR"
                    bash "$BUILD_SCRIPT"
                    docker images ruoyu-admin --format '{{.Repository}}:{{.Tag}} {{.CreatedSince}} {{.Size}}'
                '''
            }
        }

        stage('Unit Test') {
            steps {
                sh '''
                    set -e
                    mkdir -p "$REPORT_DIR"
                    cd "$SERVICE_DIR"
                    # Clean ALL stale obj/bin under ruoyu.common and this service —
                    # a previous Docker build leaves incomplete project.assets.json
                    # (empty projectReferences -> CS0234) and missing ref DLLs
                    # (obj/Release/net8.0/ref/ empty -> CS0006) for every transitively
                    # restored project, not just Tests.
                    COMMON_DIR="$REPO_DIR/src/services/ruoyu.common"
                    find "$COMMON_DIR" -type d \\( -name obj -o -name bin \\) -prune -exec rm -rf {} + 2>/dev/null || true
                    find "$SERVICE_DIR" -type d \\( -name obj -o -name bin \\) -prune -exec rm -rf {} + 2>/dev/null || true
                    dotnet restore "$TEST_PROJ" --source "$NUGET_SOURCE"
                    dotnet test "$TEST_PROJ" \
                        --configuration Release \
                        --logger 'trx;logfilename=admin-ut.trx' \
                        --results-directory "$REPORT_DIR" \
                        --no-restore
                    echo 'UT completed'
                '''
            }
        }

        stage('Deploy') {
            steps {
                sh '''
                    set -e
                    cd "$SERVICE_DIR"
                    bash "$START_SCRIPT" &
                    START_PID=$!
                    sleep 20
                    kill $START_PID 2>/dev/null || true
                    docker ps --filter 'name=ruoyu-admin' --format '{{.Names}} {{.Status}}'
                '''
            }
        }

        stage('Smoke Test') {
            steps {
                sh '''
                    set +e
                    for i in $(seq 1 30); do
                        CODE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 3 http://localhost:10901/ 2>/dev/null || echo 000)
                        if [ "$CODE" = "200" ]; then
                            echo "Admin Portal ready after ${i}s"
                            break
                        fi
                        echo "Attempt $i: HTTP $CODE, retrying..."
                        sleep 1
                    done
                    CODE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 http://localhost:10901/ 2>/dev/null || echo 000)
                    if [ "$CODE" = "200" ]; then
                        echo "Admin Portal smoke test PASSED (HTTP $CODE)"
                        exit 0
                    else
                        echo "Admin Portal smoke test FAILED (HTTP $CODE)"
                        exit 1
                    fi
                '''
            }
        }
    }

    post {
        always {
            echo "=== Collecting artifacts ==="
            script {
                sh '''
                    mkdir -p "$REPORT_DIR"
                    ls -la "$REPORT_DIR/" || true
                '''
            }
            archiveArtifacts artifacts: 'reports/**', allowEmptyArchive: true
        }
        success {
            echo 'Admin Portal pipeline PASSED: build + UT + deploy + smoke'
        }
        failure {
            echo 'Admin Portal pipeline FAILED — check logs above'
        }
    }
}
