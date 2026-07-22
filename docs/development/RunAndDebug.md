# 启动与调试 (RunAndDebug)

## 启动后端

```bash
cd src/admin_portal/backend

# 开发模式（带 Swagger UI）
dotnet run

# 指定端口
dotnet run --AdminApi:Port=5020
```

启动后访问：
- API：`http://localhost:5020/`
- Swagger UI：`http://localhost:5020/swagger`（仅 Development 环境）
- 健康检查：`curl http://localhost:5020/` → 返回 "Student Admin WebAPI is running."

## 启动前端

```bash
cd src/admin_portal/frontend

# 开发模式（热重载）
npm run dev

# 构建生产版本
npm run build
```

## 集成部署模式

将前端构建产物部署到后端 wwwroot：

```bash
cd src/admin_portal/frontend
npm run build
cp -r dist/* ../backend/wwwroot/
```

后端启动后自动提供前端静态文件和 SPA 路由回退。

## 调试技巧

### gRPC 服务不可用时的降级测试

- 使用 `USE_LOCAL_OSS=1` 可在无下游服务时启动后端
- OssAuditController 的查询/状态接口仅依赖本地数据库，可独立调试
- StudentsController / MistakeController 需要 gRPC 服务运行

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
