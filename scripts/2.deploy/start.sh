#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HTTP_PORT="8091"
IMAGE_NAME="ruoyu-admin:$(date +%Y%m%d)"
CONTAINER_NAME="ruoyu-admin"
GRPC_HOST_PORT="5005"

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
  -p "${HTTP_PORT}:5020" \
  -e TZ=Asia/Shanghai \
  -e APP_TITLE="${CONTAINER_NAME}" \
  -e GrpcService__Address="http://host.docker.internal:${GRPC_HOST_PORT}" \
  "$IMAGE_NAME"

echo "${CONTAINER_NAME} started"
echo "-> HTTP Port: ${HTTP_PORT}"
echo "-> gRPC Service: host.docker.internal:${GRPC_HOST_PORT}"
echo "-> Image: ${IMAGE_NAME}"
echo "=== Real-time Logs ==="
docker logs -f -t "$CONTAINER_NAME"
