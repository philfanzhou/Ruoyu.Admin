```json
[
  {
    "id": "task-1",
    "title": "检查遗留数据状态",
    "description": "管理员检查某条上传记录是否属于遗留数据，了解关联错题审核状态和图片路径情况",
    "endpoint": "GET /api/admin/oss-upload-records/legacy-check/{id}",
    "userStory": "作为管理员，我希望检查上传记录是否为遗留数据，以便决定是否需要清理"
  },
  {
    "id": "task-2",
    "title": "清理遗留数据",
    "description": "管理员清理遗留的上传记录数据，完成审核流程、移除图片、删除记录",
    "endpoint": "POST /api/admin/oss-upload-records/legacy-clean/{id}",
    "userStory": "作为管理员，我希望清理遗留的上传记录数据，以便保持系统数据一致性"
  }
]
```
