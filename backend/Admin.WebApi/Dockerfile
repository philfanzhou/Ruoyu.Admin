# Build context: repository root.
#   docker build -f backend/Admin.WebApi/Dockerfile -t ruoyu.admin .
# Produces a single image that serves both the API (port 5020) and the built Vue SPA from wwwroot.

# ========== Stage 1: Build Frontend ==========
FROM node:20-alpine AS frontend-build
ARG NPM_REGISTRY=https://mirrors.huaweicloud.com/repository/npm/
ENV npm_config_registry=${NPM_REGISTRY}
ENV npm_config_replace_registry_host=always
WORKDIR /frontend
COPY frontend/package*json ./
RUN npm ci --registry=${NPM_REGISTRY}
COPY frontend/ ./
RUN npm run build

# ========== Stage 2: Build & Publish Backend ==========
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src

# Restore first with csproj only, so the layer cache survives source edits.
COPY backend/Ruoyu.Admin.Common/Ruoyu.Admin.Common.csproj ./backend/Ruoyu.Admin.Common/
COPY backend/Ruoyu.Admin.Consul/Ruoyu.Admin.Consul.csproj ./backend/Ruoyu.Admin.Consul/
COPY backend/Ruoyu.Admin.ServiceClients/Ruoyu.Admin.ServiceClients.csproj ./backend/Ruoyu.Admin.ServiceClients/
COPY backend/Admin.WebApi/Admin.WebApi.csproj ./backend/Admin.WebApi/
RUN dotnet restore "backend/Admin.WebApi/Admin.WebApi.csproj"

COPY backend/Ruoyu.Admin.Common/ ./backend/Ruoyu.Admin.Common/
COPY backend/Ruoyu.Admin.Consul/ ./backend/Ruoyu.Admin.Consul/
COPY backend/Ruoyu.Admin.ServiceClients/ ./backend/Ruoyu.Admin.ServiceClients/
COPY backend/Admin.WebApi/ ./backend/Admin.WebApi/
COPY --from=frontend-build /frontend/dist ./backend/Admin.WebApi/wwwroot
RUN dotnet publish "backend/Admin.WebApi/Admin.WebApi.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

# ========== Stage 3: Runtime ==========
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=backend-build /app/publish .

EXPOSE 5020
ENV ASPNETCORE_URLS=http://+:5020

ENTRYPOINT ["dotnet", "Admin.WebApi.dll"]
