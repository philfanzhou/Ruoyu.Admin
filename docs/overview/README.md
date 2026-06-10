# 总览文档索引

本目录收拢 Admin Portal 的服务级总览类正式文档，提供全局认知、边界定义和下钻入口。

> **本目录不承载模块级实现细节。** 具体功能的需求、设计、任务、测试、约定在 [modules/](../modules/) 中维护；外部系统交互在 [Integration/](../Integration/) 中维护；数据库结构在 [database/](../database/) 中维护。

## 阅读建议

- **第一次接触**：先读 [SystemContext](./SystemContext.md) 了解服务定位和上下游，再读 [Design](./Design.md) 了解架构分层
- **查接口边界**：看 [Integration](./Integration.md) 集成矩阵
- **查数据归属**：看 [DataOwnership](./DataOwnership.md)
- **查流程**：看 [KeyFlows](./KeyFlows.md) 的关键时序图
- **查需求**：看 [Requirements](./Requirements.md) 的需求摘要，详细需求下钻到 [modules/](../modules/)
- **查设计**：看 [Design](./Design.md) 的架构概览，详细设计下钻到 [modules/](../modules/)
- **查编码规范**：看 [DotNetCodingPolicy](./DotNetCodingPolicy.md)

## 文档清单

| 文档 | 用途 | 下钻目标 |
|------|------|----------|
| [SystemContext.md](./SystemContext.md) | 服务定位、上下游调用关系、服务边界 | — |
| [Integration.md](./Integration.md) | 集成矩阵、接口边界、失败语义 | [Integration/](../Integration/) |
| [KeyFlows.md](./KeyFlows.md) | 关键跨服务时序与调用链（ASCII 图） | [modules/](../modules/) |
| [DataOwnership.md](./DataOwnership.md) | 数据主责、引用边界、双写禁区 | [database/](../database/) |
| [Requirements.md](./Requirements.md) | 服务级需求摘要（精简） | [modules/](../modules/) |
| [Design.md](./Design.md) | 服务级架构概览（精简） | [modules/](../modules/) |
| [DotNetCodingPolicy.md](./DotNetCodingPolicy.md) | .NET 编码规范 | — |
