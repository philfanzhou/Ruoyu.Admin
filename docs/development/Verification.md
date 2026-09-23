# 验证与测试 (Verification)

## 运行测试

```bash
cd backend

# 运行全部测试
dotnet test Ruoyu.Admin.sln --configuration Release

# 运行指定测试类
dotnet test Ruoyu.Admin.sln --filter "FullyQualifiedName~OssUploadRecordControllerLegacyTests"

# 运行模型测试
dotnet test Ruoyu.Admin.sln --filter "FullyQualifiedName~ModelTests"
```

## 测试项目结构

```
test/Admin.WebApi.Tests/
├── Admin.WebApi.Tests.csproj
├── Models/
│   └── ModelTests.cs              # DTO 构造函数测试
├── Controllers/
│   ├── AssignmentControllerTests.cs
│   ├── EnumOptionsControllerTests.cs          # 枚举选项聚合测试
│   ├── IdentityAccountsControllerTests.cs
│   ├── ImageControllerTests.cs                # 图片代理路径校验与 gRPC 异常测试
│   ├── MistakeControllerTests.cs
│   ├── OssAuditControllerTests.cs
│   ├── OssUploadRecordControllerLegacyTests.cs
│   ├── OssUploadRecordControllerTests.cs
│   └── StudentsControllerTests.cs
├── Middleware/
│   ├── IdentityProxyMiddlewareTests.cs
│   └── TeacherPortalProxyMiddlewareTests.cs
└── Services/
    └── OssAuditWorkerTests.cs
```

## 覆盖率

```bash
dotnet test --collect:"XPlat Code Coverage"
```

> 当前未配置覆盖率目标。建议目标：Controller 层 ≥ 80%。

## 构建验证

```bash
# Release 构建
cd backend
dotnet publish -c Release -o ./publish

# 验证构建产物
ls ./publish/Admin.WebApi.dll
```

## API 验证

后端启动后可通过 Swagger UI 或 curl 验证：

```bash
# 健康检查
curl http://localhost:5020/

# 枚举选项（无需下游服务）
curl http://localhost:5020/api/admin/enum-options

# 审计状态（仅需本地数据库）
curl http://localhost:5020/api/admin/oss-audit/status
```

## 测试覆盖现状

| 模块 | 测试文件 | 覆盖说明 |
|------|----------|----------|
| Models (DTO) | ModelTests.cs | DTO 构造函数 |
| OssUploadRecordController (Legacy) | OssUploadRecordControllerLegacyTests.cs | LegacyCheck/LegacyClean |
| OssUploadRecordController | OssUploadRecordControllerTests.cs, AssignmentControllerTests.cs | 分配/旋转/重置/列表/详情 |
| StudentsController | StudentsControllerTests.cs | ListStudents/CreateStudent/GetStudent |
| MistakeController | MistakeControllerTests.cs | GetMistakeItems/UpdateMistakeItem |
| OssAuditController | OssAuditControllerTests.cs | 审计记录管理 |
| OssAuditWorker | OssAuditWorkerTests.cs | 后台审计任务 |
| IdentityProxyMiddleware | IdentityProxyMiddlewareTests.cs | 代理转发 |
| TeacherPortalProxyMiddleware | TeacherPortalProxyMiddlewareTests.cs | 代理转发 |
| IdentityAccountsController | IdentityAccountsControllerTests.cs | 账户批量查询/关联学生 |
| EnumOptionsController | EnumOptionsControllerTests.cs | GetAll 聚合返回 5 类枚举 |
| ImageController | ImageControllerTests.cs | 路径校验（空/非法/正常）、mistakes 路径走 Mistake gRPC、其它走 Student gRPC、NotFound/其它 gRPC 异常 |

### EnumOptionsController 测试要点

- `GetAll` 返回 `EnumOptionsResponse`，包含 5 个列表：UploadStatuses、Grades、Subjects、Classifications、ReviewStatuses
- 每个列表的元素数量应与对应 `*Constants.EnglishNames` 一致
- 每个元素的 `Value`/`Name`/`DisplayName` 字段应正确映射

### ImageController 测试要点

- 路径校验：空路径 → 400；含 `..` 或以 `/`、`\` 开头 → 400
- 路径以 `mistakes/` 开头（不区分大小写）→ 调用 `MistakeGrpcService.GetPresignedUrlAsync`
- 其它路径 → 调用 `StudentLearningGrpcService.GetPresignedUrlAsync`，并传递 `size` 参数
- gRPC 返回正常 → 302 Redirect 到预签名 URL
- gRPC 抛 `NotFound` → 404
- gRPC 抛其它异常 → 502

> 详细测试计划见各模块的 05-TESTS.md
