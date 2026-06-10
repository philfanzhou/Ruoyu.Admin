# UploadRecordManagement 测试

## 现有测试覆盖

**无**。当前没有针对 UploadRecordManagement 功能（GetAllUploadRecords、ResetUploadRecordStatus、AssignUploadRecord、GetImage、RotateImage、RemoveImageFromRecord、AnalyzeUploadRecord）的单元测试。

## 缺失测试（建议补充）

### GetAllUploadRecords

- **Given** StudentManagement.GetAllUploadRecords 返回分页数据，**When** 调用 GetAllUploadRecords，**Then** 返回 200 和包含学生姓名的记录列表
- **Given** StudentManagement.GetAllUploadRecords 返回分页数据且部分学生查询失败，**When** 调用 GetAllUploadRecords，**Then** 失败学生的 studentName 回退为 studentId
- **Given** StudentManagement.GetAllUploadRecords 抛出异常，**When** 调用 GetAllUploadRecords，**Then** 返回 500
- **Given** 传入 status=-1，**When** 调用 GetAllUploadRecords，**Then** gRPC 请求的 Status 为 Unspecified
- **Given** 传入 status=1，**When** 调用 GetAllUploadRecords，**Then** gRPC 请求的 Status 为 Uploaded

### ResetUploadRecordStatus

- **Given** StudentLearning.ResetUploadRecordStatus 返回 Success=true，**When** 调用 ResetUploadRecordStatus，**Then** 返回 200 和 success=true
- **Given** StudentLearning.ResetUploadRecordStatus 返回 Success=false，**When** 调用 ResetUploadRecordStatus，**Then** 返回 400
- **Given** StudentLearning.ResetUploadRecordStatus 抛出异常，**When** 调用 ResetUploadRecordStatus，**Then** 返回 500

### AssignUploadRecord

- **Given** 上传记录存在且所有 assignment 图片索引有效，**When** 调用 AssignUploadRecord，**Then** 为每个 assignment 调用 SubmitMistakeUpload，调用 MarkUploadRecordCompleted，返回 200
- **Given** 上传记录不存在，**When** 调用 AssignUploadRecord，**Then** 返回 400
- **Given** 部分 assignment 的图片索引超出范围，**When** 调用 AssignUploadRecord，**Then** 跳过无效 assignment，返回 warnings
- **Given** SubmitMistakeUpload 返回失败，**When** 调用 AssignUploadRecord，**Then** 记录警告，allSuccess=false
- **Given** gRPC 调用抛出异常，**When** 调用 AssignUploadRecord，**Then** 返回 500

### GetImage

- **Given** path 参数为空，**When** 调用 GetImage，**Then** 返回 400
- **Given** OSS 中存在该图片，**When** 调用 GetImage，**Then** 返回图片二进制流和正确的 Content-Type
- **Given** OSS 中不存在该图片，**When** 调用 GetImage，**Then** 返回 404
- **Given** IOssService 抛出异常，**When** 调用 GetImage，**Then** 返回 500

### RotateImage

- **Given** id 不是合法 GUID，**When** 调用 RotateImage，**Then** 返回 400
- **Given** StudentId 为空，**When** 调用 RotateImage，**Then** 返回 400
- **Given** StudentLearning.RotateUploadImage 返回 Success=true，**When** 调用 RotateImage，**Then** 返回 200
- **Given** StudentLearning.RotateUploadImage 返回 Success=false，**When** 调用 RotateImage，**Then** 返回 400
- **Given** gRPC 调用抛出异常，**When** 调用 RotateImage，**Then** 返回 500

### RemoveImageFromRecord

- **Given** studentId 为空，**When** 调用 RemoveImageFromRecord，**Then** 返回 400
- **Given** StudentLearning.RemoveImageFromRecord 返回 Success=true，**When** 调用 RemoveImageFromRecord，**Then** 返回 200 和 recordDeleted/remainingImageCount
- **Given** StudentLearning.RemoveImageFromRecord 返回 Success=false，**When** 调用 RemoveImageFromRecord，**Then** 返回 400
- **Given** gRPC 调用抛出异常，**When** 调用 RemoveImageFromRecord，**Then** 返回 500

### AnalyzeUploadRecord

- **Given** StudentManagement.AnalyzeUploadRecord 返回分析结果，**When** 调用 AnalyzeUploadRecord，**Then** 返回 200 和分组信息
- **Given** StudentManagement.AnalyzeUploadRecord 抛出异常，**When** 调用 AnalyzeUploadRecord，**Then** 返回 500
