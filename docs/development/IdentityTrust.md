# 显式 JWT issuer 信任

`Ruoyu.Admin.Common.Authentication.IdentityTokenValidationParametersFactory.Create` 的公共 JWT 参数只信任 `IdentityService:Issuer` 与 `IdentityService:AdditionalValidIssuers`。创建时去空、Trim、按 Ordinal 去重，生成同一个只读快照供 `ValidIssuers` 和 `IssuerValidator` 使用。大小写或尾斜线差异均不匹配。随后修改原 options、框架替换 `ValidIssuer` / `ValidIssuers` 或 discovery 更新不会扩大已创建参数的信任集合。

真实 `JwtBearerHandler` 会把 discovery issuer 带入校验参数；严格委托仍只接受该显式快照。discovery 提供签名密钥，不授予额外 issuer。显式主 issuer 和迁移 issuer 在签名、audience、有效期正确时通过；同一有效密钥签发的 discovery-only issuer 返回 401。坏签名、错误 audience 和过期 token 继续拒绝，`ClockSkew`、名称及角色 claim 配置保持。

若显式配置与实际签发 issuer 不一致，校验抛出 `SecurityTokenInvalidIssuerException`，固定消息为 `Token issuer must match IdentityService:Issuer or IdentityService:AdditionalValidIssuers.`，不回显 issuer、token 或密钥。部署负责人应核对经批准的精确 issuer；迁移期间仅添加批准的旧 issuer，窗口结束后清空迁移列表。升级后原来依赖 discovery 自动扩充信任的调用会被拒绝，不能通过添加未批准 issuer 消除负例。

该 helper 的公开 API 和配置键不变，也不更改 Admin 入口认证模式。Admin 的托管登录由 `SignaCore.Client.AspNetCore` 独立约束 `IdentityService:Authority`：Discovery issuer 必须与 Authority 同源且 ID/access token 的 issuer 精确校验，公共 helper 的迁移列表不扩充托管登录信任。`AdminSessionAccessor` 读取服务器票据，不验证浏览器 JWT。专属回归宿主的 Bearer 200 只证明公共 helper 合同，不代表 Admin API 授予入站 Bearer 权限；Admin 的唯一服务器会话模式见 [Deployment.md](./Deployment.md)。

本次没有数据库迁移、持久状态或新取消入口。快照可并发读取，每次创建相互独立。不防御签名密钥泄露，也不防御受信进程代码移除严格委托、关闭 issuer 校验或主动改写内部信任数据；调用方必须保留这些校验。代码或整版镜像回滚可能恢复 discovery 信任放大风险，不是安全保证，也不作为恢复旧登录模式的依据。

验证入口是 `backend/Tests/Authentication/IdentityTokenValidationParametersFactoryTests.cs` 与 `ExplicitIssuerJwtBearerTests.cs`，后者注册独立真实 handler，使用内存合成 RSA 签名及静态 discovery metadata；不修改 Admin 宿主、不接触数据库或 OSS、不输出 token。原托管 OIDC 的错误 issuer、坏 metadata、取消及零票据回归继续由 `AdminOidcTests` 覆盖。

## Mistake 出站服务器票据

`MistakeService:UseSessionToken=true` 时，专用 handler 每次在当前请求 scope 调用 `AdminSessionAccessor.AuthenticateAsync`，额外读取当前 `ITicketStore` 票据。框架的同请求认证缓存、`HttpContext.User`、TrustedSessionKey 或池化 handler 都不能替代最新 ticket；换票、撤票、坏 stamp/issuer、当前白名单移除、缺/畸形 token 或到期均在发送前拒绝。只发服务器 access token 到固定 HTTPS Mistake origin 与既有 method/path，禁 Cookie/redirect/loggers/telemetry；AppSecret 不作为用户身份。`false` 仅保留原 Mistake 匿名兼容，不改变永久入站 Session-only。

目标 Mistake 必须独立信任精确 Issuer/Authority、ADMIN audience 与所需 role。Admin 本地白名单通过不保证目标授权：真实目标 401/403 对 Admin 为安全 502。详情 GET 404、合法空结果和明确业务 400/409 与认证/传输失败分别处理。配置、取消/单次发送以及真实组合验收要求见 [Deployment](./Deployment.md#mistake-出站会话ikjnxa)。
