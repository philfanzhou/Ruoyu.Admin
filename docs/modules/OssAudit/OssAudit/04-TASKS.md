# OssAudit 任务清单

```json
[
  {
    "id": "T1",
    "title": "查看审计记录列表",
    "description": "管理员通过 GET /api/admin/oss-audit/records 查看僵尸文件列表，支持按状态和桶筛选，返回分页结果及统计信息",
    "endpoint": "GET /api/admin/oss-audit/records",
    "method": "GET"
  },
  {
    "id": "T2",
    "title": "手动触发审计",
    "description": "管理员通过 POST /api/admin/oss-audit/trigger 手动触发一次OSS审计扫描，需检查当前无运行中的审计任务",
    "endpoint": "POST /api/admin/oss-audit/trigger",
    "method": "POST"
  },
  {
    "id": "T3",
    "title": "查看审计状态",
    "description": "管理员通过 GET /api/admin/oss-audit/status 查看当前审计运行状态、上次完成/失败时间、待处理数量",
    "endpoint": "GET /api/admin/oss-audit/status",
    "method": "GET"
  },
  {
    "id": "T4",
    "title": "清理单条僵尸文件",
    "description": "管理员通过 POST /api/admin/oss-audit/records/{id}/resolve 清理单条Pending记录，需验证文件未被Student或Mistake服务引用，然后删除OSS对象并移除DB记录",
    "endpoint": "POST /api/admin/oss-audit/records/{id}/resolve",
    "method": "POST"
  },
  {
    "id": "T5",
    "title": "忽略单条僵尸文件",
    "description": "管理员通过 POST /api/admin/oss-audit/records/{id}/ignore 忽略单条记录，设置Status=Ignored并记录备注",
    "endpoint": "POST /api/admin/oss-audit/records/{id}/ignore",
    "method": "POST"
  },
  {
    "id": "T6",
    "title": "批量清理僵尸文件",
    "description": "管理员通过 POST /api/admin/oss-audit/records/batch-resolve 批量清理多条记录，一次性获取引用路径后逐条校验和删除",
    "endpoint": "POST /api/admin/oss-audit/records/batch-resolve",
    "method": "POST"
  },
  {
    "id": "T7",
    "title": "定时自动审计",
    "description": "OssAuditWorker 作为 BackgroundService 按配置的调度时间（默认凌晨2:00）自动执行审计扫描",
    "endpoint": "N/A",
    "method": "N/A"
  }
]
```
