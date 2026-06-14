#!/bin/bash
set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
IMAGE_TAG="20260502"
IMAGE_NAME="ruoyu.admin:${IMAGE_TAG}"
CONTAINER_NAME="ruoyu-admin"
NETWORK_NAME="ruoyu-net"
ADMIN_HTTP_PORT="10901"

STUDENT_GRPC_ADDR="ruoyu-student:5005"
MISTAKE_GRPC_ADDR="ruoyu-mistake:5006"
TEACHER_API_ADDR="ruoyu-teacher-api:5004"
TEACHER_ADMIN_API_KEY=""
IDENTITY_HTTP_ADDR="ruoyu-identity:5002"
IDENTITY_APP_ID=""
IDENTITY_APP_SECRET=""

# OSS 凭证（审计+运维所需，权限应限制为只读+有限写）
OSS_ENDPOINT="ruoyu-seaweedfs:8333"
OSS_ACCESS_KEY="seaweedfs_admin"
OSS_SECRET_KEY="seaweedfs_admin"
OSS_BUCKET="ruoyu-study"

POSTGRES_HOST="ruoyu-postgres"
POSTGRES_PORT="5432"
POSTGRES_DB="ruoyu_admin"
POSTGRES_USER="postgres"
POSTGRES_PASSWORD="postgres"

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
  -e StudentGrpcService__Address="http://${STUDENT_GRPC_ADDR}" \
  -e MistakeGrpcService__Address="http://${MISTAKE_GRPC_ADDR}" \
  -e IdentityService__Address="http://${IDENTITY_HTTP_ADDR}" \
  -e IdentityService__AppId="${IDENTITY_APP_ID}" \
  -e IdentityService__AppSecret="${IDENTITY_APP_SECRET}" \
  -e TeacherPortal__Address="http://${TEACHER_API_ADDR}" \
  -e TeacherPortal__AdminApiKey="${TEACHER_ADMIN_API_KEY}" \
  -e Oss__Endpoint="${OSS_ENDPOINT}" \
  -e Oss__AccessKey="${OSS_ACCESS_KEY}" \
  -e Oss__SecretKey="${OSS_SECRET_KEY}" \
  -e Oss__BucketName="${OSS_BUCKET}" \
  -e ConnectionStrings__AuditDb="Host=${POSTGRES_HOST};Port=${POSTGRES_PORT};Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}" \
  "$IMAGE_NAME"

echo "${CONTAINER_NAME} started"
docker logs -f -t "$CONTAINER_NAME"
