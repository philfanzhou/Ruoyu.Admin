# 受管错题指派

Student 与 Mistake 的 HTTP 契约由 Ruoyu.Study 拥有。Admin 只聚合可信来源、固定请求并展示结果；不新增数据库迁移、幂等表、receipt 写入或来源释放逻辑。

## 配置与认证

`Mistake:ManagedAssignmentEnabled` 默认 `false`，只接受布尔值。开启时必须显式配置 HTTPS origin `MistakeService:Url`（无路径、用户信息、query、fragment）和现有 `IdentityService:Authority/Audience`，保留 `RequireHttpsMetadata=true`。真实证书须匹配主机名并进入运行环境信任链。不开启自动重定向、Cookie 转发、证书验证绕过或隐式 localhost 回退。

列表复用现有 Student HTTP client。写入使用独立 `IManagedMistakeHttpClient`，仅调用 `POST /api/mistakes/upload`。服务端 session 模式取现有 Admin session boundary 已经检查管理员权限和 unsafe-method CSRF 的可信 access token；legacy 模式取现有 Bearer handler 已验证的管理员原 token。该 token 每个请求单独设置；不接受 body 的 actor/role，不签发 worker 凭据，不附加到其他路由或 redirect。

## 可信来源与固定提案

详情直接使用 `GET /api/admin/oss-upload-records` 的列表项，没有新增 GET 单条详情路由。项内保留 `imagePaths`，新增 `contentRevision`、有序 `imageEntries[{path,type}]`、`assignmentProtocol`。开启时缺少 items/revision/entries、非法 source/student GUID、空类型或坏路径返回明确失败；正常空 items 数组仍是合法空页。查询姓名失败可回退 studentId，不能据此构造来源 metadata。

UI 仅允许精确 `type === "mistake"` 项。原 index 用于当前表单选择，发送时固定精确路径，去重只按 Ordinal；不 trim、改大小写或改 Unicode。关闭能力返回 `assignmentProtocol="legacy"`；未知或缺失协议不可指派。

现有 `POST /api/admin/oss-upload-records/{id}/assign` 的 managed body：

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

source/student/revision/key 均为非零 GUID。1–100 组，每组唯一 key、subject 1–9、grade 1–12、1–100 个非空路径（每项最多4096字符），comments 最多4096字符。comments 转为 producer 的 rootCause，null 固定为空串。body 是提案，Mistake snapshot 才是路径、学生和分类的授权事实。共享原图的不同合法 group 不按 sourceId 粗去重。

提交和重试不再 GET 当前 Student 或按新索引重建；始终发送原 revision、paths、key 和分类。关闭能力时 managed 输入503；开启但缺字段400，均不落回 legacy Submit。

## 结果、恢复与边界

每组保留 `requestKey,state,errorKind,createdItemIds,statusCode`。仅真实 HTTP200、外层 success=true、内层 success=true、合法非零且不重复的非空 GUID IDs 成为 Completed。body 有64KiB上限，单次发送及读取合计30秒；没有自动 retry。明确业务拒绝为 Failed；5xx、未知503、断连、超时、取消、坏 JSON、缺 IDs 为 Unknown。第一组未完成即停止后续发送，剩余为 NotAttempted。整批全部 Completed 才 success=true。

UI 显示每组真实 IDs 和安全错误种类，不把 HTTP200 等同整批成功。发送中锁定选择/分类/编辑及图片变更，双击只发一次。显式重试整个原 batch，已完成组取 producer 原结果，已有 IDs 不能被新的不一致响应抹去。Unknown 未解决时不能覆盖原操作。切行/关闭抽屉以 generation 隔离迟到结果，取消定位当前操作；页面内恢复入口保留固定操作，包括当前源已改变或删除时。取消等待不证明远端未提交。

这些记录只在页面内存，整页关闭后不保证仍存在。Mistake canonical alias/result 是唯一 durable authority；Admin 不创造第二套 journal，不自动 seal、不移图、不删除来源、不执行 legacy cleanup。原 legacy 数据合并、physical GC、共享环境 rollout 属于独立范围。

## 验证入口

后端 Release build/test；`ManagedMistakeHttpClientTests` 校验具体 HTTP 编解码和失败，`ManagedAssignmentApiTests` 通过实际 MVC/session/CSRF/Bearer 路径，`ManagedAssignmentOptionsTests` 校验启用前置。前端执行 `npm ci`、`npm run build`、`npm test`；fixed-operation 与 Drawer 测试覆盖固定载荷、部分结果、Unknown恢复、双击、取消及迟到代次。

真实验收使用独立 owning fixture：固定实际 Admin head 和 Study provider head、正常 TLS、实际 Student/Mistake、PG/S3 和正式身份注册。验证合法两组、部分409/5xx/断连/坏 body、commit 后丢响应及历史重放、alias并发/封口/auto竞争、两认证管线和 CSRF。保留 raw 失败与恢复、确切命令/digest/UTC/0skip、owned资源与无关栈前后安全摘要。unit/stub 的绿色结果不代替这些业务证据。
