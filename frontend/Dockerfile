# Build context: repository root.
#   docker build -f frontend/Dockerfile -t ruoyu.admin.web .
# Standalone Nginx image: serves the built SPA and reverse-proxies /api/ to the Admin API.
# Not needed for the integrated single-container deployment (see backend/Admin.WebApi/Dockerfile).

FROM node:20-alpine AS frontend-build
ARG NPM_REGISTRY=https://mirrors.huaweicloud.com/repository/npm/
ENV npm_config_registry=${NPM_REGISTRY}
ENV npm_config_replace_registry_host=always
WORKDIR /app
COPY frontend/package*json ./
RUN npm ci --registry=${NPM_REGISTRY}
COPY frontend/ ./
RUN npm run build

FROM nginx:1.30.0-alpine AS final
COPY --from=frontend-build /app/dist /usr/share/nginx/html
COPY frontend/nginx.conf /etc/nginx/nginx.conf

EXPOSE 80
