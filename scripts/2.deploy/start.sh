#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ADMIN_HTTP_PORT="10901"
IMAGE_NAME="ruoyu.admin:$(date +%Y%m%d)"
CONTAINER_NAME="ruoyu-admin"
NETWORK_NAME="ruoyu-net"

IDENTITY_HTTP_HOST="quantumzhou-identity"
IDENTITY_HTTP_PORT="5002"
STUDENT_GRPC_HOST="ruoyu-student"
STUDENT_GRPC_PORT="5005"

docker network inspect "$NETWORK_NAME" >/dev/null 2>&1 || docker network create "$NETWORK_NAME"

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
  --network "$NETWORK_NAME" \
  -p "${ADMIN_HTTP_PORT}:5020" \
  -e TZ=Asia/Shanghai \
  -e APP_TITLE="${CONTAINER_NAME}" \
  -e StudentGrpcService__Address="http://${STUDENT_GRPC_HOST}:${STUDENT_GRPC_PORT}" \
  -e IdentityService__Address="http://${IDENTITY_HTTP_HOST}:${IDENTITY_HTTP_PORT}" \
  "$IMAGE_NAME"

echo "${CONTAINER_NAME} started"
echo "-> HTTP Port: ${ADMIN_HTTP_PORT}"
echo "-> Student gRPC: ${STUDENT_GRPC_HOST}:${STUDENT_GRPC_PORT}"
echo "-> Identity HTTP: ${IDENTITY_HTTP_HOST}:${IDENTITY_HTTP_PORT}"
echo "-> Network: ${NETWORK_NAME}"
echo "-> Image: ${IMAGE_NAME}"
echo "=== Real-time Logs ==="
docker logs -f -t "$CONTAINER_NAME"
