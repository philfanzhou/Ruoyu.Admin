# 验证与测试 (Verification)

## 运行测试

```bash
cd src/admin_portal

# 运行全部测试
dotnet test

# 运行指定测试类
dotnet test --filter "FullyQualifiedName~OssUploadRecordControllerLegacyTests"

# 运行模型测试
dotnet test --filter "FullyQualifiedName~ModelTests"
```

## 测试项目结构

```
test/Admin.WebApi.Tests/
├── Admin.WebApi.Tests.csproj
├── Models/
│   └── ModelTests.cs              # DTO 构造函数测试
└── Controllers/
    └── OssUploadRecordControllerLegacyTests.cs  # LegacyCheck/LegacyClean 测试
```

## 覆盖率

```bash
dotnet test --collect:"XPlat Code Coverage"
```

> 当前未配置覆盖率目标。建议目标：Controller 层 ≥ 80%。

## 构建验证

```bash
# Release 构建
cd src/admin_portal/backend
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

| 模块 | 有测试 | 无测试 |
|------|--------|--------|
| Models (DTO) | ModelTests.cs | - |
| OssUploadRecordController (Legacy) | LegacyTests.cs | 其他端点 |
| StudentsController | - | 全部 |
| MistakeController | - | 全部 |
| OssAuditController | - | 全部 |
| OssAuditWorker | - | 全部 |
| IdentityProxyMiddleware | - | 全部 |
| TeacherPortalProxyMiddleware | - | 全部 |
| ImageController | - | 全部 |
| EnumOptionsController | - | 全部 |
| IdentityAccountsController | - | 全部 |

> 详细测试计划见各模块的 05-TESTS.md
