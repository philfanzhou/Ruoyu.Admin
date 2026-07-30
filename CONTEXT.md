# Storage Audit

Storage Audit 定义如何发现和处置没有业务引用的存储对象。它不拥有学习业务数据或存储对象的业务生命周期。

## Language

**Audit Run**:
一次完整的存储引用核对活动。
_Avoid_: Background Job、Process Log

**Audit Record**:
Audit Run 对一个可疑存储对象形成的待处置发现。
_Avoid_: OSS Object、Business Record

**Orphan Object**:
未被任何已知业务记录引用的存储对象。
_Avoid_: Deleted Object、Missing Reference

**Resolution**:
对 Audit Record 作出的删除、忽略或保留决定及其结果。
_Avoid_: Mistake Review、Homework Review
