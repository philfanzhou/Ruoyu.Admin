#!/bin/bash
set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
IMAGE_TAG="20260502"
IMAGE_NAME="ruoyu.admin:${IMAGE_TAG}"
CONTAINER_NAME="ruoyu-admin"
NETWORK_NAME="ruoyu-net"
ADMIN_HTTP_PORT="10901"
ADMIN_API_PORT="5020"

CONSUL_HTTP_ADDR="${CONSUL_HTTP_ADDR:-host.docker.internal:8500}"
CONSUL_TOKEN="${CONSUL_TOKEN:-}"

DB_NAME="ruoyu_study_admin"

# Sensitive credentials stay in start.sh (not in Consul):
# - IdentityService AppId/AppSecret (admin portal acts as Identity gateway client;
#   also used for the admin callback that injects role:admin into the JWT)
IDENTITY_APP_ID="${IDENTITY_APP_ID:-}"
IDENTITY_APP_SECRET="${IDENTITY_APP_SECRET:-}"

# Non-sensitive endpoints (IdentityService.Authority, StudentService.Url, MistakeService.Url,
# TeacherPortal.Url, AssistantPortal.Url) are sourced from Consul
# config/ruoyu/service-endpoints.json.

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
  --add-host=host.docker.internal:host-gateway \
  -p "${ADMIN_HTTP_PORT}:${ADMIN_API_PORT}" \
  -e TZ=Asia/Shanghai \
  -e CONSUL_HTTP_ADDR="${CONSUL_HTTP_ADDR}" \
  -e CONSUL_TOKEN="${CONSUL_TOKEN}" \
  -e Database__Name="${DB_NAME}" \
  -e APP_TITLE="${CONTAINER_NAME}" \
  -e AdminApi__Port="${ADMIN_API_PORT}" \
  -e IdentityService__AppId="${IDENTITY_APP_ID}" \
  -e IdentityService__AppSecret="${IDENTITY_APP_SECRET}" \
  "$IMAGE_NAME"

echo "${CONTAINER_NAME} started"
docker logs -f -t "$CONTAINER_NAME"
