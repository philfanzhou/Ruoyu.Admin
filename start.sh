#!/bin/bash
set -euo pipefail

IMAGE_NAME="ruoyu.admin:20260502"
CONTAINER_NAME="ruoyu-admin"
PORT="5020"

CONSUL_HTTP_ADDR="${CONSUL_HTTP_ADDR:-127.0.0.1:8500}"
CONSUL_TOKEN="${CONSUL_TOKEN:-}"

DB_NAME="ruoyu_admin"

IDENTITY_APP_ID="${IDENTITY_APP_ID:-}"
IDENTITY_APP_SECRET="${IDENTITY_APP_SECRET:-}"
: "${IDENTITY_APP_ID:?IDENTITY_APP_ID must come from the Admin Portal deployment secret}"
: "${IDENTITY_APP_SECRET:?IDENTITY_APP_SECRET must come from the Admin Portal deployment secret}"
echo "Using Admin Portal Identity AppId ${IDENTITY_APP_ID:0:4}...${IDENTITY_APP_ID: -4}"

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
  -p "${PORT}:5020" \
  -e TZ=Asia/Shanghai \
  -e CONSUL_HTTP_ADDR="${CONSUL_HTTP_ADDR}" \
  -e CONSUL_TOKEN="${CONSUL_TOKEN}" \
  -e Database__Name="${DB_NAME}" \
  -e APP_TITLE="${CONTAINER_NAME}" \
  -e IdentityService__AppId="${IDENTITY_APP_ID}" \
  -e IdentityService__AppSecret="${IDENTITY_APP_SECRET}" \
  "$IMAGE_NAME"
