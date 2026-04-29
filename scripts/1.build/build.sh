#!/bin/bash
set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PORTAL_DIR="$REPO_ROOT/admin_portal"
STUDENT_CONTRACT_DIR="$REPO_ROOT/backend/ruoyu.student/src/Contract"
DATE=$(date +%Y%m%d)
IMAGE_NAME="ruoyu-admin:${DATE}"

if [ ! -f "$PORTAL_DIR/AdminPortal.sln" ]; then
    echo "Error: AdminPortal.sln not found under build context: $PORTAL_DIR"
    echo "Expected repository layout: <repo-root>/admin_portal and <repo-root>/backend/ruoyu.student"
    exit 1
fi

if [ ! -f "$STUDENT_CONTRACT_DIR/Ruoyu.Study.Student.Contract.csproj" ]; then
    echo "Error: Student contract project not found: $STUDENT_CONTRACT_DIR"
    echo "The Docker build context must include backend/ruoyu.student."
    exit 1
fi

IMAGE_EXISTS=$(docker images -q "$IMAGE_NAME" 2>/dev/null)

if [ -n "$IMAGE_EXISTS" ]; then
    echo "Image $IMAGE_NAME already exists, attempting to remove..."
    if ! docker rmi "$IMAGE_NAME"; then
        echo "Error: Failed to remove existing image $IMAGE_NAME. It may be in use by a container."
        exit 1
    fi
    echo "Existing image removed."
fi

echo "=========================================="
echo "Building Unified Image: $IMAGE_NAME"
echo "Using AdminPortal.sln solution"
echo "Build context: $REPO_ROOT"
echo "Running tests during build..."
echo "=========================================="

docker build -f "$SCRIPT_DIR/Dockerfile" -t "$IMAGE_NAME" "$REPO_ROOT"

echo "Unified image built: $IMAGE_NAME"

echo "Cleaning build cache..."
docker builder prune -af --force

echo "Done."
