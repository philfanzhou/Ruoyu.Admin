# 部署与运维

## 构建与部署

- Dockerfile：`scripts/9.admin-portal/1.build/Dockerfile`
- 部署脚本：`scripts/9.admin-portal/2.deploy/start.sh`

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

### 下游服务配置

| 依赖服务 | 地址 | 协议 |
|----------|------|------|
| Student 服务 | `http://ruoyu-student:5005` | gRPC |
| Mistake 服务 | `http://ruoyu-mistake:5006` | gRPC |
| Identity 服务 | `http://ruoyu-identity:5002` | HTTP |
| Teacher Portal | `http://ruoyu-teacher-portal-api:5004` | HTTP |

## 健康检查

- 端点：`/health`（端口 5020）

## 数据库备份与恢复

```bash
# 备份
docker exec ruoyu-postgres pg_dump -U postgres ruoyu_study_admin | gzip > backup_admin_$(date +%Y%m%d_%H%M%S).sql.gz

# 恢复
gunzip -c backup_admin_20240101_020000.sql.gz | docker exec -i ruoyu-postgres psql -U postgres -d ruoyu_study_admin
```
