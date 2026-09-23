#!/bin/bash
# Build the unified Ruoyu.Admin image (API + built Vue SPA in wwwroot).
#
# Usage:
#   ./scripts/build.sh              # build ruoyu.admin:<IMAGE_TAG>
#   IMAGE_TAG=20260924 ./scripts/build.sh
set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

IMAGE_TAG="${IMAGE_TAG:-20260502}"
IMAGE_NAME="ruoyu.admin:${IMAGE_TAG}"

if [ ! -f "$REPO_ROOT/backend/Admin.WebApi/Admin.WebApi.csproj" ]; then
    echo "Error: Admin.WebApi.csproj not found at $REPO_ROOT/backend/Admin.WebApi"
    exit 1
fi

echo "=========================================="
echo "Building unified image: $IMAGE_NAME"
echo "Build context:          $REPO_ROOT"
echo "=========================================="

docker build -f "$REPO_ROOT/backend/Admin.WebApi/Dockerfile" -t "$IMAGE_NAME" "$REPO_ROOT"

echo "Unified image built: $IMAGE_NAME"
echo "Done."
