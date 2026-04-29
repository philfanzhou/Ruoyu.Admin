# ========== Stage 1: Build Frontend ==========
FROM node:20-alpine AS frontend-build
WORKDIR /frontend
COPY frontend/package*json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# ========== Stage 2: Build & Publish Backend ==========
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src
COPY . .
RUN dotnet restore "AdminPortal.sln"
RUN dotnet test "AdminPortal.sln" -c Release --no-restore
COPY --from=frontend-build /backend/wwwroot ./backend/wwwroot
RUN dotnet publish "backend/Admin.WebApi.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

# ========== Stage 3: Runtime ==========
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=backend-build /app/publish .

EXPOSE 5020
ENV ASPNETCORE_URLS=http://+:5020

HEALTHCHECK --interval=30s --timeout=3s --start-period=60s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://localhost:5020/ || exit 1

ENTRYPOINT ["dotnet", "Admin.WebApi.dll"]
