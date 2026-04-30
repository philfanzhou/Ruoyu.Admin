#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ADMIN_HTTP_PORT="10901"
IMAGE_NAME="ruoyu.admin:$(date +%Y%m%d)"
CONTAINER_NAME="ruoyu-admin"
IDENTITY_HTTP_PORT="10891"
STUDENT_GRPC_PORT="10892"

if [ -n "$(docker ps -q --filter "name=^/${CONTAINER_NAME}$")" ]; then
    echo "Container is already running, stopping it..."
    docker stop "$CONTAINER_NAME"
fi
if [ -n "$(docker ps -aq --filter "name=^/${CONTAINER_NAME}$")" ]; then
    echo "Removing old container..."
    docker rm "$CONTAINER_NAME"
fi

docker run -d \
  --name "$CONTAINER_NAME" \
  --restart unless-stopped \
  --add-host=host.docker.internal:host-gateway \
  -p "${ADMIN_HTTP_PORT}:5020" \
  -e TZ=Asia/Shanghai \
  -e APP_TITLE="${CONTAINER_NAME}" \
  -e StudentGrpcService__Address="http://host.docker.internal:${STUDENT_GRPC_PORT}" \
  -e IdentityService__Address="http://host.docker.internal:${IDENTITY_HTTP_PORT}" \
  "$IMAGE_NAME"

echo "${CONTAINER_NAME} started"
echo "-> HTTP Port: ${ADMIN_HTTP_PORT}"
echo "-> Student gRPC: host.docker.internal:${STUDENT_GRPC_PORT}"
echo "-> Identity HTTP: host.docker.internal:${IDENTITY_HTTP_PORT}"
echo "-> Image: ${IMAGE_NAME}"
echo "=== Real-time Logs ==="
docker logs -f -t "$CONTAINER_NAME"
