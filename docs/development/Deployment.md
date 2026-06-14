# 部署与运维

## 构建与部署

- Dockerfile：`scripts/9.admin-portal/1.build/Dockerfile`
- 部署脚本：`scripts/9.admin-portal/2.deploy/start.sh`

## 部署模式

### 模式一：前后端集成部署（原有）

前端构建产物部署到后端 `wwwroot` 目录，由 ASP.NET Core 提供静态文件服务。单一容器部署。

### 模式二：Nginx 容器前后端分离部署（oss-nginx-proxy 变更）

新增 Nginx 容器，前端静态文件由 Nginx 提供，API 请求代理到后端，OSS 对象通过 `/oss/` 路径代理到 SeaweedFS。

**Nginx 容器职责**：
- 提供前端静态文件服务
- `/api/` 请求代理到 Admin Portal 后端（`:5020`）
- `/oss/` 请求代理到 SeaweedFS（`:8333`），strip `/oss/` 前缀

**Nginx OSS 代理配置**：

```nginx
location /oss/ {
    proxy_pass http://ruoyu-seaweedfs:8333/;
    proxy_set_header Host ruoyu-seaweedfs:8333;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    add_header Cache-Control "public, max-age=3600";
}
```

关键配置说明：
- `proxy_pass` 末尾 `/`：strip `/oss/` 前缀，如 `/oss/ruoyu-study/mistakes/xxx/image.jpg` → `/ruoyu-study/mistakes/xxx/image.jpg`
- `proxy_set_header Host ruoyu-seaweedfs:8333`：S3 签名验证基于 Host 头，必须设为 SeaweedFS 内部地址
- `Cache-Control: public, max-age=3600`：1 小时公共缓存，减少 SeaweedFS 请求压力

## 配置项

### 数据库连接

```json
{
  "ConnectionStrings": {
    "Default": "Host=ruoyu-postgres;Port=5432;Database=ruoyu_study_admin;Username=postgres;Password=postgres"
  }
}
```

### 服务端口

| 端口 | 协议 | 用途 |
|------|------|------|
| 5020 | HTTP | 管理门户后端 + 健康检查 |
| 5175 | HTTP | 管理门户前端 |
| 80 | HTTP | Nginx 容器（前端静态文件 + API/OSS 反向代理） |

### 下游服务配置

| 依赖服务 | 地址 | 协议 |
|----------|------|------|
| Student 服务 | `http://ruoyu-student:5005` | gRPC |
| Mistake 服务 | `http://ruoyu-mistake:5006` | gRPC |
| Identity 服务 | `http://ruoyu-identity:5002` | HTTP |
| Teacher Portal | `http://ruoyu-teacher-portal-api:5004` | HTTP |
| SeaweedFS (OSS) | `http://ruoyu-seaweedfs:8333` | S3 API |

### PublicEndpoint 配置

使用 Nginx 容器部署时，需配置 `Oss:PublicEndpoint` 使预签名 URL 指向 Nginx 代理地址：

```json
{
  "Oss": {
    "Endpoint": "ruoyu-seaweedfs:8333",
    "AccessKey": "seaweedfs_admin",
    "SecretKey": "seaweedfs_admin",
    "BucketName": "ruoyu-study",
    "PublicEndpoint": "https://admin.example.com"
  }
}
```

`PublicEndpoint` 非空时，`GetPresignedUrlAsync` 生成的预签名 URL 会将 scheme+host 替换为 `{PublicEndpoint}/oss`，前端通过 Nginx `/oss/` 代理访问 OSS 对象。

## 健康检查

- 端点：`/health`（端口 5020）

## 数据库备份与恢复

```bash
# 备份
docker exec ruoyu-postgres pg_dump -U postgres ruoyu_study_admin | gzip > backup_admin_$(date +%Y%m%d_%H%M%S).sql.gz

# 恢复
gunzip -c backup_admin_20240101_020000.sql.gz | docker exec -i ruoyu-postgres psql -U postgres -d ruoyu_study_admin
```
