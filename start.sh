#!/bin/bash
set -euo pipefail

# The target and configuration are explicit; preflight uses this exact image/configuration.
IMAGE_NAME="${IMAGE_NAME:-}"
CONTAINER_NAME="${CONTAINER_NAME:-ruoyu-admin}"
PORT="${PORT:-5020}"
ENV_FILE="${ENV_FILE:-}"
CONFIG_FILE="${CONFIG_FILE:-}"
CACHE_DIR="${CACHE_DIR:-}"
AUTHORITY="${IDENTITY_AUTHORITY:-}"
REDIRECT_URI="${ADMIN_OIDC_REDIRECT_URI:-}"
POST_LOGOUT_URI="${ADMIN_OIDC_POST_LOGOUT_REDIRECT_URI:-}"
fail() { echo "RUOYU_ADMIN_LAUNCH_CONFIG_INVALID" >&2; exit 2; }
while (($#)); do
  (($# >= 2)) || fail
  case "$1" in
    --image) IMAGE_NAME="$2";;
    --container) CONTAINER_NAME="$2";;
    --port) PORT="$2";;
    --env-file) ENV_FILE="$2";;
    --config-file) CONFIG_FILE="$2";;
    --cache-dir) CACHE_DIR="$2";;
    --authority) AUTHORITY="$2";;
    --redirect-uri) REDIRECT_URI="$2";;
    --post-logout-redirect-uri) POST_LOGOUT_URI="$2";;
    *) fail;;
  esac
  shift 2
done
[[ -n "$IMAGE_NAME" && "$IMAGE_NAME" != -* && "$IMAGE_NAME" != *[[:space:]]* ]] || fail
[[ "$CONTAINER_NAME" =~ ^[a-zA-Z0-9][a-zA-Z0-9_.-]*$ ]] || fail
[[ "$PORT" =~ ^[0-9]+$ && "$PORT" -ge 1 && "$PORT" -le 65535 ]] || fail
common=( --label "ruoyu.admin.instance=$CONTAINER_NAME" -e TZ=Asia/Shanghai -e "APP_TITLE=$CONTAINER_NAME" )
if [[ -n "$ENV_FILE" ]]; then
  [[ -f "$ENV_FILE" && -r "$ENV_FILE" ]] || fail
  ENV_FILE="$(realpath -- "$ENV_FILE")"
  common+=( --env-file "$ENV_FILE" )
fi
if [[ -n "$CONFIG_FILE" ]]; then
  [[ -f "$CONFIG_FILE" && -r "$CONFIG_FILE" ]] || fail
  CONFIG_FILE="$(realpath -- "$CONFIG_FILE")"
  [[ "$CONFIG_FILE" != *,* ]] || fail
  common+=( --mount "type=bind,src=$CONFIG_FILE,dst=/app/appsettings.json,readonly" )
fi
# -e NAME passes environment values without including secret values in docker argv or output.
export IdentityService__AppId="${IDENTITY_APP_ID:-${IdentityService__AppId:-}}"
export IdentityService__AppSecret="${IDENTITY_APP_SECRET:-${IdentityService__AppSecret:-}}"
export IdentityService__Authority="${AUTHORITY:-${IdentityService__Authority:-}}"
export AdminOidc__RedirectUri="${REDIRECT_URI:-${AdminOidc__RedirectUri:-}}"
export AdminOidc__PostLogoutRedirectUri="${POST_LOGOUT_URI:-${AdminOidc__PostLogoutRedirectUri:-}}"
for key in IdentityService__AppId IdentityService__AppSecret IdentityService__Authority AdminOidc__RedirectUri AdminOidc__PostLogoutRedirectUri CONSUL_HTTP_ADDR CONSUL_TOKEN CONSUL_HOST CONSUL_PORT CONSUL_KV_PREFIX CONSUL_TIMEOUT_MS CONSUL_RETRY_COUNT CONSUL_ENABLE_CACHE; do
  if [[ -n "${!key:-}" ]]; then common+=( -e "$key" ); fi
done
common+=( -e "Database__Name=${DB_NAME:-ruoyu_admin}" )
preflight_cache=()
runtime_cache=()
if [[ -n "$CACHE_DIR" ]]; then
  [[ -d "$CACHE_DIR" && -r "$CACHE_DIR" && -w "$CACHE_DIR" ]] || fail
  CACHE_DIR="$(realpath -- "$CACHE_DIR")"
  [[ "$CACHE_DIR" != *,* ]] || fail
  common+=( -e CONSUL_CACHE_DIR=/app/data/consul )
  preflight_cache+=( --mount "type=bind,src=$CACHE_DIR,dst=/app/data/consul,readonly" )
  runtime_cache+=( --mount "type=bind,src=$CACHE_DIR,dst=/app/data/consul" )
fi
# Bash 3.2 treats an empty array as unset under nounset; expand optional mounts only when set.
# A configuration failure exits here, before even inspecting, stopping or removing the target.
docker run --rm --name "${CONTAINER_NAME}-auth-preflight" "${common[@]}" ${preflight_cache[@]+"${preflight_cache[@]}"} "$IMAGE_NAME" --validate-auth-config

# The explicitly named target must belong to this launcher (new deployments) or be an
# existing Ruoyu.Admin product image (upgrade from the original launcher).
old_id="$(docker ps -aq --filter "name=^/${CONTAINER_NAME}$")"
if [[ -n "$old_id" ]]; then
  old_owner="$(docker inspect --format '{{index .Config.Labels "ruoyu.admin.launcher"}}' "$old_id")"
  old_image="$(docker inspect --format '{{.Config.Image}}' "$old_id")"
  [[ "$old_owner" == true || "$old_image" == ruoyu.admin:* || "$old_image" == ghcr.io/philfanzhou/ruoyu.admin:* || "$old_image" == ghcr.io/philfanzhou/ruoyu.admin@sha256:* ]] || fail
  docker stop "$old_id"
  docker rm "$old_id"
fi
docker run -d --name "$CONTAINER_NAME" --label ruoyu.admin.launcher=true \
  --restart unless-stopped -p "${PORT}:5020" "${common[@]}" ${runtime_cache[@]+"${runtime_cache[@]}"} "$IMAGE_NAME"
