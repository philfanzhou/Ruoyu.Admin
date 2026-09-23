#!/bin/bash
# Build the standalone Nginx frontend image (SPA + /api/ reverse proxy).
# Only needed when the frontend is deployed separately from the API.
#
# Usage:
#   ./scripts/build-web.sh
#   IMAGE_TAG=20260924 ./scripts/build-web.sh
set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

IMAGE_TAG="${IMAGE_TAG:-20260502}"
IMAGE_NAME="ruoyu.admin.web:${IMAGE_TAG}"

if [ ! -f "$REPO_ROOT/frontend/package.json" ]; then
    echo "Error: frontend/package.json not found at $REPO_ROOT/frontend"
    exit 1
fi

echo "=========================================="
echo "Building web image: $IMAGE_NAME"
echo "Build context:      $REPO_ROOT"
echo "=========================================="

docker build -f "$REPO_ROOT/frontend/Dockerfile" -t "$IMAGE_NAME" "$REPO_ROOT"

echo "Web image built: $IMAGE_NAME"
echo "Done."
