# Identity 信任

`Ruoyu.Admin.Common` 的旧显式 issuer JWT 消费栈（`AddRuoyuJwtBearer`、`IdentityTokenValidationParametersFactory`、`IdentityAuthenticationOptions*`、`ClaimsPrincipalExtensions`、`RoleConstants`）已删除（#103）：该栈从未在 `Program.cs` 注册，删除前后入站认证路径无任何差异。

Admin 的唯一入站认证是服务器会话 + `SignaCore.Client.AspNetCore` 托管 OIDC，issuer 信任由该客户端包独立约束 `IdentityService:Authority`：discovery issuer 必须与 Authority 同源且 token issuer 精确校验。`AdminSessionAccessor` 读取服务器票据，不验证浏览器 JWT；任何入站 Authorization 固定 401，不存在浏览器 JWT 回退模式。唯一服务器会话模式见 [Deployment.md](./Deployment.md)。

验证入口：`backend/Tests/Integration/AdminOidcTests.cs` 覆盖错误 issuer、坏 metadata、取消及零票据回归；`AdminSessionApiTests.cs` 与 `AdminAssociationSessionTests.cs` 在测试宿主中配置 "Bearer" 方案 options 并断言其认证回调零次触发（"Bearer" 方案的金丝雀）。原 `IdentityTokenValidationParametersFactoryTests` 与 `ExplicitIssuerJwtBearerTests` 测的是被删除的公共 helper 合同，已随之移除。

## Mistake 出站服务器票据

`MistakeService:UseSessionToken=true` 时，专用 handler 每次在当前请求 scope 调用 `AdminSessionAccessor.AuthenticateAsync`，额外读取当前 `ITicketStore` 票据。框架的同请求认证缓存、`HttpContext.User`、TrustedSessionKey 或池化 handler 都不能替代最新 ticket；换票、撤票、坏 stamp/issuer、当前白名单移除、缺/畸形 token 或到期均在发送前拒绝。只发服务器 access token 到固定 HTTPS Mistake origin 与既有 method/path，禁 Cookie/redirect/loggers/telemetry；AppSecret 不作为用户身份。`false` 仅保留原 Mistake 匿名兼容，不改变永久入站 Session-only。

目标 Mistake 必须独立信任精确 Issuer/Authority、ADMIN audience 与所需 role。Admin 本地白名单通过不保证目标授权：真实目标 401/403 对 Admin 为安全 502。详情 GET 404、合法空结果和明确业务 400/409 与认证/传输失败分别处理。配置、取消/单次发送以及真实组合验收要求见 [Deployment](./Deployment.md#mistake-出站会话ikjnxa)。
