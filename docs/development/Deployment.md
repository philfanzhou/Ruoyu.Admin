# 部署与运维

## 构建与启动

- 集成镜像：`backend/Admin.WebApi/Dockerfile`，先构建 Vue 前端，再把产物复制到 API 的 `wwwroot`。构建入口 `./scripts/build.sh`，产物 `ruoyu.admin:${IMAGE_TAG}`（`IMAGE_TAG` 默认 `20260502`）。
- 启动脚本：仓库根 `start.sh`。
- 容器：`ruoyu-admin`，容器内监听 5020，默认映射到宿主机 10901。
- 容器使用 Docker 默认 bridge，不依赖 `ruoyu-net` 或容器名解析；跨主机依赖通过 Consul KV 中的局域网地址访问。
- 独立前端镜像位于 `frontend/Dockerfile`，构建入口 `./scripts/build-web.sh`，产物 `ruoyu.admin.web:${IMAGE_TAG}`。其 Nginx 只提供静态文件和 Admin API 代理，不负责 `/oss/`。
- 两个 Dockerfile 的构建上下文都是**仓库根**，且都受仓库根 `.dockerignore` 约束。

### 服务标识

迁出 monorepo 时服务标识从 `Ruoyu.Study.AdminPortal` 改为 `Ruoyu.Admin`，出现在两处：

| 位置 | 键 | 影响 |
|------|-----|------|
| `appsettings.json` | `Serilog:WriteTo:GrafanaLoki:labels[service]` | Loki 日志标签。**已有的 Grafana 查询、面板和告警若按 `service="Ruoyu.Study.AdminPortal"` 过滤，必须同步改名**，否则新日志查不到 |
| `appsettings.json` | `Consul:ServiceName` | 仅作标识。Consul KV 读取只使用 `Consul:KvPrefix`（`config/ruoyu`），`RuoyuConsulKvLoader.BuildPrefixes` 不消费 `ServiceName`，因此改名不影响配置加载 |

若部署环境希望保留旧标识以复用既有看板：`Consul:ServiceName` 可用环境变量 `CONSUL_SERVICE_NAME` 覆盖（见 `RuoyuConsulOptions.Bind`），Loki 标签则直接改 `appsettings.json` 或用配置覆盖，两者都不需要改代码。

## 配置加载

Admin API 启动时通过 Consul `config/ruoyu/*` 加载 PostgreSQL、OSS 和下游服务共享配置。`start.sh` 注入 Consul 地址、数据库名 `ruoyu_admin` 和 Identity 应用凭据。仓库中的 `127.0.0.1` 只是占位默认值，**不是任何真实部署的地址**；部署时通过 `CONSUL_HTTP_ADDR` 指向实际 Consul 地址。

### OSS

```json
{
  "Oss": {
    "InternalEndpoint": "10.20.30.40:8333",
    "InternalSecure": false,
    "AccessKey": "seaweedfs_admin",
    "SecretKey": "seaweedfs_admin",
    "BucketName": "ruoyu-study",
    "PublicBaseUrl": "https://oss.example.com/oss"
  }
}
```

- `InternalEndpoint/InternalSecure`：Admin 后端审计、清理、下载和迁移辅助使用的实际 S3 连接。
- `PublicBaseUrl`：`GetPresignedUrlAsync` 使用的公共签名地址。
- 浏览器访问 `/oss/` 时由 User Web Nginx 统一代理；Admin Nginx 不维护 SeaweedFS 上游地址。
- `USE_LOCAL_OSS=1` 时使用 `LocalFileOssService`，目录由 `OSS_LOCAL_PATH` 指定，默认 `data/oss`。

### 下游服务

| 依赖 | 仓库占位默认值 | 协议 |
|------|----------|------|
| Student | `http://127.0.0.1:5005` | HTTP |
| Mistake | `http://127.0.0.1:5007` | HTTP |
| Identity | `http://127.0.0.1:5002` | HTTP |
| Teacher Portal | `http://127.0.0.1:5004` | HTTP |
| Assistant Portal | `http://127.0.0.1:5021` | HTTP |
| SeaweedFS | `Oss:InternalEndpoint` | S3 |

上表均为占位值，实际地址一律由部署配置下发，本仓库不记录任何真实环境地址。

下游地址分别来自 Consul 的 `StudentService:Url`、`MistakeService:Url`、`IdentityService:Authority`、`TeacherPortal:Url` 和 `AssistantPortal:Url`。跨主机迁移时需要更新 live KV（需要时再同步 seed KV），并重启 Admin Portal 才会加载新值。

## 健康检查

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:10901/
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:10901/api/admin/students
```

首页应返回 200；未携带登录凭据访问受保护的 Admin API 应返回 401。再结合 `docker inspect ruoyu-admin` 的运行状态、重启次数和启动日志判断服务是否稳定。

## 旧批改派生图清理

包含 ADR-0007 的 Admin API 在启动后延迟 2 分钟，自动扫描 `uploads/homework` 并删除严格匹配旧 `.../reviews/{revisionId}.jpg` UUID 路径规则的批改派生图，关联缩略图和残留 OSS 审计记录同步删除。该流程可重试且不匹配学生提交原图；部署后检查日志 `Obsolete homework review image cleanup completed` 获取删除数量或失败告警。

## 数据库备份与恢复

```bash
docker exec ruoyu-postgres pg_dump -U postgres ruoyu_admin | gzip > backup_admin.sql.gz
gunzip -c backup_admin.sql.gz | docker exec -i ruoyu-postgres psql -U postgres -d ruoyu_admin
```
