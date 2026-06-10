# MistakeManagement 测试

## 现有测试覆盖

**无**。当前没有针对 MistakeController 任何端点的单元测试。

## 缺失测试（建议补充）

### GetMistakeItems

- **Given** Mistake.GetMistakeItemList 返回分页数据，**When** 调用 GetMistakeItems，**Then** 返回 200 和包含学生姓名的错题列表
- **Given** Mistake.GetMistakeItemList 返回分页数据且部分学生查询失败，**When** 调用 GetMistakeItems，**Then** 失败学生的 studentName 回退为 studentId
- **Given** page=0，**When** 调用 GetMistakeItems，**Then** page 被重置为 1
- **Given** size=0，**When** 调用 GetMistakeItems，**Then** size 被重置为 10
- **Given** size=200，**When** 调用 GetMistakeItems，**Then** size 被限制为 100
- **Given** 传入 reviewStatus=1，**When** 调用 GetMistakeItems，**Then** gRPC 请求的 ReviewStatus 为 Confirmed
- **Given** Mistake.GetMistakeItemList 抛出异常，**When** 调用 GetMistakeItems，**Then** 返回 500

### GetMistakeItem

- **Given** Mistake.GetMistakeItem 返回包含 sourceRegions 和 boundingBox 的数据，**When** 调用 GetMistakeItem，**Then** 返回 200 和包含 sourceRegions 的详情
- **Given** Mistake.GetMistakeItem 返回 boundingBox 为 null 的 region，**When** 调用 GetMistakeItem，**Then** 返回的 boundingBox 为 null
- **Given** 学生查询失败，**When** 调用 GetMistakeItem，**Then** studentName 回退为 studentId
- **Given** Mistake.GetMistakeItem 抛出异常，**When** 调用 GetMistakeItem，**Then** 返回 500

### GetMistakesByUploadId

- **Given** Mistake.GetMistakeItemsByUpload 返回错题列表，**When** 调用 GetMistakesByUploadId，**Then** 返回 200 和包含学生姓名的错题列表
- **Given** Mistake.GetMistakeItemsByUpload 抛出异常，**When** 调用 GetMistakesByUploadId，**Then** 返回 500

### UpdateMistakeItem

- **Given** Mistake.UpdateMistakeItem 返回更新后的数据，**When** 调用 UpdateMistakeItem，**Then** 返回 200 和包含学生姓名的更新结果
- **Given** 请求中 StudentId/Subject/Grade 为 null，**When** 调用 UpdateMistakeItem，**Then** gRPC 请求中对应字段为空字符串或 0
- **Given** Mistake.UpdateMistakeItem 抛出异常，**When** 调用 UpdateMistakeItem，**Then** 返回 500

### MigrateImages

- **Given** 所有图片路径已在 mistakes/ 下，**When** 调用 MigrateImages，**Then** 返回 200，migratedCount=0
- **Given** 有图片在 uploads/ 下，**When** 调用 MigrateImages，**Then** 执行 CopyObjectAsync + DeleteAsync，更新 sourceRegions，返回 migratedCount
- **Given** 同一旧路径出现多次，**When** 调用 MigrateImages，**Then** 只迁移一次（使用缓存）
- **Given** CopyObjectAsync 抛出异常，**When** 调用 MigrateImages，**Then** 返回 500
- **Given** DeleteAsync 抛出异常，**When** 调用 MigrateImages，**Then** 返回 500
- **Given** GetMistakeItem 抛出异常，**When** 调用 MigrateImages，**Then** 返回 500
- **Given** UpdateMistakeItem（更新 sourceRegions）抛出异常，**When** 调用 MigrateImages，**Then** 返回 500
