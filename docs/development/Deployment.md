# 部署与运维

## 构建与启动

- 集成镜像：`backend/Admin.WebApi/Dockerfile`，先构建 Vue 前端，再把产物复制到 API 的 `wwwroot`。
- 启动脚本：仓库根 `src/admin_portal/start.sh`。
- 容器：`ruoyu-admin`，容器内监听 5020，默认映射到宿主机 10901。
- 独立前端镜像位于 `frontend/Dockerfile`，其 Nginx 只提供静态文件和 Admin API 代理，不负责 `/oss/`。

## 配置加载

Admin API 启动时通过 Consul `config/ruoyu/*` 加载 PostgreSQL、OSS 和下游服务共享配置。`start.sh` 注入 Consul 地址、数据库名和 Identity 应用凭据。

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

| 依赖 | 默认地址 | 协议 |
|------|----------|------|
| Student | `http://127.0.0.1:5005`（示例内网地址） | HTTP |
| Mistake | `http://127.0.0.1:5007`（仓库假内网示例） | HTTP |
| Identity | `http://127.0.0.1:5002`（仓库假内网示例） | HTTP |
| Teacher Portal | `http://ruoyu-teacher-api:5004` | HTTP |
| Assistant Portal | `http://ruoyu-assistant-api:5021` | HTTP |
| SeaweedFS | `Oss:InternalEndpoint` | S3 |

Student 地址来自 Consul `StudentService:Url`。跨主机迁移时需要同时更新 seed/live KV，并重启 Admin Portal API 才会加载新值。

## 健康检查

```text
GET /health
```

## 数据库备份与恢复

```bash
docker exec ruoyu-postgres pg_dump -U postgres ruoyu_admin | gzip > backup_admin.sql.gz
gunzip -c backup_admin.sql.gz | docker exec -i ruoyu-postgres psql -U postgres -d ruoyu_admin
```
