# 受管错题指派

Student 与 Mistake 的 HTTP 接口由 Ruoyu.Study 维护。Admin 只聚合可信来源、固定请求并展示结果；不新增数据库迁移、幂等表、回执写入或来源释放逻辑。

## 配置与认证

`Mistake:ManagedAssignmentEnabled` 默认 `false`，只接受布尔值。开启时必须显式配置 HTTPS origin `MistakeService:Url`（无路径、用户信息、query、fragment）和现有 `IdentityService:Authority/Audience`，保留 `RequireHttpsMetadata=true`。真实证书须匹配主机名并进入运行环境信任链。不启用自动重定向、Cookie 转发、证书验证绕过或隐式 localhost 回退。普通出站传输的配置调整不放宽本能力的 HTTPS 要求。

列表复用现有 Student HTTP 客户端。写入使用独立 `IManagedMistakeHttpClient`，仅调用 `POST /api/mistakes/upload`。当前只接受服务端会话：从已检查管理员权限和非安全 HTTP 方法 CSRF 的 Admin 会话边界取得可信 access token。任何入站 Authorization，即使伴随合法会话和 CSRF，也返回 401；API 会话能力关闭时固定返回 503 `session_api_disabled`，不恢复已退役 Bearer 认证。token 每个请求单独设置；不接受请求 body 的 actor/role，不签发 worker 凭据，也不将 token 附加到其他路由或重定向。

## 可信来源与固定请求

详情直接使用 `GET /api/admin/oss-upload-records` 的列表项，没有新增单条详情 GET 路由。列表项保留 `imagePaths`，新增 `contentRevision`、有序 `imageEntries[{path,type}]` 和 `assignmentProtocol`。能力开启时，缺少 items/revision/entries、source/student GUID 非法、类型为空或路径非法，均明确失败；正常的空 items 数组仍是合法空页。查询姓名失败时可显示 studentId，不能用姓名查询结果构造来源元数据。

界面只允许选择精确 `type === "mistake"` 的项。原 index 仅用于当前表单选择；发送时固定精确路径，去重只按 Ordinal 字符串比较，不 trim、不改大小写或 Unicode。关闭能力时返回 `assignmentProtocol="legacy"`；未知或缺失协议不可指派。

现有 `POST /api/admin/oss-upload-records/{id}/assign` 接受如下 managed body：

```json
{
  "mode": "managed-v1",
  "studentId": "11111111-1111-4111-8111-111111111111",
  "expectedContentRevision": "22222222-2222-4222-8222-222222222222",
  "assignments": [{
    "requestKey": "33333333-3333-4333-8333-333333333333",
    "subject": 2,
    "grade": 7,
    "sourcePaths": ["uploads/题目/A.PNG"],
    "comments": "固定原因"
  }]
}
```

source/student/revision/key 均为非零 GUID。每批 1–100 组，各组 key 唯一，subject 为 1–9，grade 为 1–12，comments 最多 4096 字符。comments 映射为 Mistake 的 rootCause，null 固定为空串。每组必须在 `sourcePaths` 与 `sourceRegions` 中二选一：

- `sourcePaths`：1–100 个非空精确路径，每项最多 4096 字符，表示整图。
- `sourceRegions`：1–100 个区域，每项包含非空的精确 `sourceImagePath`（最多 4096 字符）和可空 `boundingBox`。`boundingBox=null` 表示整图；矩形坐标 `x1,y1,x2,y2` 必须为非负整数，且 `x1<x2`、`y1<y2`。例如 `{"sourceImagePath":"uploads/题目/A.PNG","boundingBox":{"x1":0,"y1":0,"x2":100,"y2":100}}`。发送区域时，下游 `imagePaths` 为空。

body 表达管理员的指派请求；路径、学生与分类的授权依据仍是 Mistake 读取的来源快照。不同合法分组不按 sourceId 粗略去重。Mistake 使用区域单元（`atom`）判断分组：其中编码了原路径、整图 `Full` 或裁剪矩形、源图字节长度和 SHA-256，不能将它等同于一个图片路径。同一原图的不同合法裁剪区域可组成不同分组；冲突判断仍遵循上游规范。

提交与重试不再 GET 当前 Student，也不按新索引重建；始终发送原 revision、路径或区域、key 和分类。关闭能力时，managed 输入返回 503；开启但缺字段时返回 400，均不回退到 legacy Submit。

## 结果、恢复与边界

每组保留 `requestKey,state,errorKind,createdItemIds,statusCode`。仅真实 HTTP 200、外层 success=true、内层 success=true，且 IDs 为合法、非零、不重复的非空 GUID 集合时，状态才成为 Completed。响应 body 上限为 64 KiB，单次发送与读取合计 30 秒，不自动重试。明确业务拒绝为 Failed；5xx、未知 503、断连、超时、取消、非法 JSON 或缺失 IDs 为 Unknown。第一组未完成即停止发送后续组，剩余组为 NotAttempted；整批全部 Completed 才返回 success=true。

界面显示每组真实 IDs 和不含敏感值的错误种类，不把 HTTP 200 等同于整批成功。发送中锁定选择、分类、编辑和图片变更；双击只发送一次。用户显式重试时发送整个原批次；已完成组按 Mistake 的规则读取原结果，已有 IDs 不能被新的不一致响应抹去。Unknown 未解决时不能覆盖原操作。切行或关闭抽屉时，递增操作编号 `generation`，只允许属于当前操作的响应更新界面，避免延迟返回的旧响应覆盖新内容。取消仅停止当前操作的等待；页面内的恢复入口保留固定请求，即使当前来源已改变或删除也可查看。取消等待不证明远端未提交。

这些记录仅保存在页面内存中，整页关闭后不保证恢复。永久保存分组、请求关联与结果的是 Mistake：它按来源、修订和区域确定规范分组（canonical）；新 requestKey 对应同一分组和载荷时，可以关联（alias）并读取持久保存的原条目和结果。Admin 不新增数据库或浏览器操作日志表，也不主动封闭来源（seal）：来源交接后，上游拒绝新分组，已保存的请求或分组结果仍先按其规则读取和重放。Admin 不增加来源交接调用，不移图、不删除来源，也不执行旧清理。旧数据合并、物理垃圾回收和共享环境部署属于其他任务。

## 验证入口与历史验收

后端执行 Release build/test。`ManagedMistakeHttpClientTests` 检查具体 HTTP 编解码与失败；`ManagedAssignmentApiTests` 使用实际 MVC、可信会话和 CSRF 验证成功路径，并检查任意 Bearer 拒绝、API 会话关闭 503、受管能力关闭 503、非法 body 400、非法元数据 502 及拒绝时零出站；`ManagedAssignmentOptionsTests` 检查启用条件。前端执行 `npm ci`、`npm run build`、`npm test`；固定操作与抽屉测试覆盖固定载荷、部分结果、Unknown 恢复、双击、取消及旧响应延迟返回。

真实验收使用由执行者创建并负责清理的独立测试环境，记录实际 Admin 与 Study 提交、正常 TLS、真实 Student/Mistake、PostgreSQL/S3 和身份注册。验证合法两组、部分 409/5xx/断连/非法 body、提交后丢失响应与历史重放、同分组请求关联的并发、来源封闭后重放、自动处理与人工指派竞争，以及唯一会话认证和 CSRF。保存失败与恢复的原始证据、确切命令、镜像 digest、UTC 时间、零跳过的验证结果，以及本次测试资源和无关环境在测试前后的状态摘要；凭据、个人数据和原始数据库备份不得写入 Issue 或日志。单元测试或替身通过不能代替这些业务验证。

历史上，[IKJKML](https://gitee.com/philfanzhou/Ruoyu.Study/issues/IKJKML) 要求同原图的两组裁剪，但 Study `5c84d43d` 的固定 POST upload 只能传整图路径，不能表达裁剪矩形。该限制已由上游 `SourceRegions` 接口扩展解除；[PR #77](https://github.com/philfanzhou/Ruoyu.Admin/pull/77) 于 2026-10-07 合并，Admin 已同步支持两种输入，六项原验收结果见该 PR。原“两认证管线”验收按 #40/#71–#74 的唯一会话认证要求执行，不恢复退役 Bearer。

下游业务条目合法删除后的请求重放，与 Storage Audit 的 `resolve` 是不同操作：前者按 Mistake 持久记录和上游规则返回结果；后者始终拒绝对象清理，完整引用时返回 409。#77 的真实验收记录不能代替当前部署环境回归；后续部署回归由 [#24](https://github.com/philfanzhou/Ruoyu.Admin/issues/24) 跟踪。
