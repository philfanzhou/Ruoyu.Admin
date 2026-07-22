# 迁移历史

## 迁移策略

本项目**不使用** EF Core Migrations（`Add-Migration` / `Update-Database`），而是采用 `DatabaseInitializer` 在应用启动时执行手动 DDL 来创建和管理表结构。

## 初始化机制

### DatabaseInitializer

位于 `Ruoyu.Study.Common` 项目中，在 `Program.cs` 启动时调用：

```csharp
await DatabaseInitializer.InitializeAsync(db, logger, tableName => tableName switch
{
    "OssAuditRuns" => @"CREATE TABLE IF NOT EXISTS ...",
    _ => null
});
```

### 初始化流程

1. 应用启动时，通过 DI 获取 `AuditDbContext` 实例
2. 调用 `DatabaseInitializer.InitializeAsync`，传入自定义 DDL 回调
3. `DatabaseInitializer` 内部调用 `context.Database.EnsureCreated()` 创建由 EF Core 管理的表（`OssAuditRecords`）
4. 对于 `OssAuditRuns`，通过回调函数执行手动 `CREATE TABLE IF NOT EXISTS` DDL
5. 所有 DDL 均使用 `IF NOT EXISTS` 语义，支持幂等执行

### 表创建方式对比

| 表名 | 创建方式 | 原因 |
|------|----------|------|
| `OssAuditRecords` | EF Core `EnsureCreated` 自动创建 | 实体配置在 `AuditDbContext.OnModelCreating` 中完整定义 |
| `OssAuditRuns` | `DatabaseInitializer` 手动 DDL | 使用 `GENERATED ALWAYS AS IDENTITY`，EF Core 的 `EnsureCreated` 不支持此特性 [推断] |

## 变更历史

| 日期 | 变更内容 | 影响表 |
|------|----------|--------|
| [待确认] | 初始创建数据库和表结构 | `OssAuditRecords`, `OssAuditRuns` |

## 注意事项

- 由于不使用 EF Migrations，表结构变更需要手动编写 DDL 并更新 `DatabaseInitializer` 回调
- `CREATE TABLE IF NOT EXISTS` 不会修改已存在的表结构，如需变更列定义需要手动 `ALTER TABLE`
- 生产环境部署时，需注意手动执行 DDL 变更或重建数据库
