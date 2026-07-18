# ========== Stage 1: Build Frontend ==========
FROM node:20-alpine AS frontend-build
ARG NPM_REGISTRY=https://mirrors.huaweicloud.com/repository/npm/
ENV npm_config_registry=${NPM_REGISTRY}
ENV npm_config_replace_registry_host=always
WORKDIR /frontend
COPY src/admin_portal/frontend/package*json ./
RUN npm ci --registry=${NPM_REGISTRY}
COPY src/admin_portal/frontend/ ./
RUN npm run build

# ========== Stage 2: Build & Publish Backend ==========
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src

COPY src/admin_portal/backend/Admin.WebApi/Admin.WebApi.csproj ./src/admin_portal/backend/Admin.WebApi/
COPY src/ruoyu.common/backend/Ruoyu.Study.Common/Ruoyu.Study.Common.csproj ./src/ruoyu.common/backend/Ruoyu.Study.Common/
COPY src/ruoyu.common/backend/Ruoyu.Study.MistakeBff.HttpClients/Ruoyu.Study.MistakeBff.HttpClients.csproj ./src/ruoyu.common/backend/Ruoyu.Study.MistakeBff.HttpClients/
COPY src/ruoyu.common/backend/Ruoyu.Study.Consul.Shared/Ruoyu.Study.Consul.Shared.csproj ./src/ruoyu.common/backend/Ruoyu.Study.Consul.Shared/
RUN dotnet restore "src/admin_portal/backend/Admin.WebApi/Admin.WebApi.csproj"

COPY src/admin_portal/backend/Admin.WebApi/ ./src/admin_portal/backend/Admin.WebApi/
COPY src/ruoyu.common/backend/Ruoyu.Study.Common/ ./src/ruoyu.common/backend/Ruoyu.Study.Common/
COPY src/ruoyu.common/backend/Ruoyu.Study.MistakeBff.HttpClients/ ./src/ruoyu.common/backend/Ruoyu.Study.MistakeBff.HttpClients/
COPY src/ruoyu.common/backend/Ruoyu.Study.Consul.Shared/ ./src/ruoyu.common/backend/Ruoyu.Study.Consul.Shared/
COPY --from=frontend-build /frontend/dist ./src/admin_portal/backend/Admin.WebApi/wwwroot
RUN dotnet publish "src/admin_portal/backend/Admin.WebApi/Admin.WebApi.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

# ========== Stage 3: Runtime ==========
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=backend-build /app/publish .

EXPOSE 5020
ENV ASPNETCORE_URLS=http://+:5020

ENTRYPOINT ["dotnet", "Admin.WebApi.dll"]
