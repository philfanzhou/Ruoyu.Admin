# 启动与调试 (RunAndDebug)

## 启动后端

```bash
cd backend

# 开发模式（带 Swagger UI）
dotnet run --project Admin.WebApi/Admin.WebApi.csproj
```

`backend/` 下是一个多项目解决方案，`dotnet run` 必须显式指定项目。

监听端口 **5020 是 `Program.cs` 里的 `const int httpPort`，硬编码且不可配置**。`appsettings.json` 中的 `AdminApi:Port` 是遗留死配置，没有任何代码读取它，改它不会改变监听端口。

启动前置条件：`IdentityService:AppId` 与 `IdentityService:AppSecret` 必须已配置，否则宿主在启动阶段直接抛 `InvalidOperationException`。本地开发可通过环境变量注入：

```bash
IdentityService__AppId=dev-app-id \
IdentityService__AppSecret=dev-app-secret \
dotnet run --project Admin.WebApi/Admin.WebApi.csproj
```

启动后访问：
- API：`http://localhost:5020/`
- Swagger UI：`http://localhost:5020/swagger`（仅 Development 环境）
- 健康检查：`curl http://localhost:5020/` → 返回 "Student Admin WebAPI is running."

## 启动前端

```bash
cd frontend

# 开发模式（热重载）
npm run dev

# 构建生产版本
npm run build
```

开发服务器监听 8090，并把 `/api/identity`、`/api/admin`、`/api/teacher-portal`、`/api/assistant-portal` 代理到 `http://localhost:5020`。

## 集成部署模式

将前端构建产物部署到后端 wwwroot：

```bash
cd frontend
npm run build
mkdir -p ../backend/Admin.WebApi/wwwroot
cp -r dist/* ../backend/Admin.WebApi/wwwroot/
```

后端启动后自动提供前端静态文件和 SPA 路由回退。`wwwroot/` 已在 `.gitignore` 与 `.dockerignore` 中排除，Docker 构建时由 `backend/Admin.WebApi/Dockerfile` 的 frontend-build 阶段直接注入，不依赖手工拷贝。

## 调试技巧

### 下游服务不可用时的降级测试

- 使用 `USE_LOCAL_OSS=1` 可把 `IOssService` 换成本地目录实现（`OSS_LOCAL_PATH`，默认 `data/oss`），从而在没有 S3 兼容存储时调试 Storage Audit。
- `OssAuditController` 的 `GET records` / `GET status` / `POST records/{id}/ignore` 只依赖本地 `ruoyu_admin` 数据库，可独立调试；`POST trigger` 需要全部下游与 OSS 可达，`POST records/{id}/resolve` 与 `POST records/batch-resolve` 会**真实删除对象存储中的文件**。
- `StudentsController`、`MistakeController`、`ImageController`、`StudentAssociationsController`、`OssUploadRecordController` 都需要对应的 Student（:5005）/ Mistake（:5007）HTTP 服务实际可达，否则返回 502。
- `HomeworkReferenceClient` 需要 Homework（:5009）可达；不可达时审计运行会标记为 Failed，而不是跳过。
- 三个代理中间件在下游不可达时返回 502，未配置地址时返回 503。

### 日志级别调整

在 `appsettings.Development.json` 中覆盖：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```
