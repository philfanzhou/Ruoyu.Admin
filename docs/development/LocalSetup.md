# 本地环境搭建 (LocalSetup)

## 前置依赖

| 依赖 | 版本 | 用途 |
|------|------|------|
| .NET SDK | 8.0+ | 后端编译运行 |
| Node.js | 18+ | 前端构建（可选） |
| PostgreSQL | 12+ | 审计数据库（生产模式） |

> 测试环境可使用 SQLite，无需 PostgreSQL。

## 后端配置

1. 复制配置文件：
   ```bash
   cd admin_portal/backend
   # appsettings.json 已包含开发默认值，可直接使用
   ```

2. 关键配置项（`appsettings.json`）：

| 配置键 | 默认值 | 说明 |
|--------|--------|------|
| `AdminApi:Port` | 5020 | API 监听端口 |
| `ConnectionStrings:AuditDb` | PostgreSQL 连接串 | 审计数据库；改为 `Data Source=admin.db` 使用 SQLite |
| `StudentGrpcService:Address` | `http://localhost:5005` | Student gRPC 服务 |
| `MistakeGrpcService:Address` | `http://localhost:5006` | Mistake gRPC 服务 |
| `IdentityService:Address` | `http://localhost:5002` | Identity HTTP 服务 |
| `TeacherPortal:Address` | `http://localhost:5004` | Teacher Portal HTTP 服务 |

3. 环境变量覆盖（可选）：
   ```bash
   export USE_LOCAL_OSS=1           # 使用本地文件系统替代 S3
   export OSS_LOCAL_PATH=data/oss   # 本地 OSS 存储路径
   export ConnectionStrings__AuditDb="Data Source=admin.db"  # 使用 SQLite
   ```

## 前端配置

```bash
cd admin_portal/frontend
npm install
```

前端开发服务器默认 `http://localhost:5173`，API 请求代理到后端 5020 端口。

## 下游服务依赖

Admin Portal 依赖以下下游服务运行：

| 服务 | 端口 | 必要性 | 不可用时影响 |
|------|------|--------|-------------|
| Student Service | 5005 | 必须 | 学生管理、上传记录、审计 Resolve 均不可用 |
| Mistake Service | 5006 | 必须 | 错题管理不可用；审计可运行但可能误报 |
| Identity Service | 5002 | 可选 | 身份代理返回 502；账户批量查询返回空列表 |
| Teacher Portal | 5004 | 可选 | 教师门户代理返回 502/503 |

> 详细配置说明见 [deployment.md](../deployment.md)
