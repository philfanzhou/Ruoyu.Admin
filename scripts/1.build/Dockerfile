# ========== Stage 1: Build Frontend ==========
FROM node:20-alpine AS frontend-build
WORKDIR /frontend
COPY admin_portal/frontend/package*json ./
RUN npm ci
COPY admin_portal/frontend/ ./
RUN npm run build

# ========== Stage 2: Build & Publish Backend ==========
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src

# Copy project files for restore layer caching
COPY admin_portal/AdminPortal.sln .
COPY admin_portal/backend/Admin.WebApi.csproj ./backend/
COPY backend/ruoyu.student/src/Contract/Ruoyu.Study.Student.Contract.csproj ./backend/ruoyu.student/src/Contract/
COPY admin_portal/test/Admin.WebApi.Tests/Admin.WebApi.Tests.csproj ./test/Admin.WebApi.Tests/
RUN dotnet restore "AdminPortal.sln"

# Copy all source files
COPY admin_portal/backend/ ./backend/
COPY backend/ruoyu.student/src/Contract/ ./backend/ruoyu.student/src/Contract/
COPY admin_portal/test/ ./test/
COPY --from=frontend-build /backend/wwwroot ./backend/wwwroot
RUN dotnet test "AdminPortal.sln" -c Release --no-restore
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
