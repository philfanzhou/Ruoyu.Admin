```json
[
  {
    "id": "task-1",
    "title": "浏览上传记录列表",
    "description": "管理员分页浏览所有学生的上传记录，按状态和学生筛选",
    "endpoint": "GET /api/admin/oss-upload-records",
    "userStory": "作为管理员，我希望查看学生的上传记录列表，以便了解上传情况"
  },
  {
    "id": "task-2",
    "title": "查看上传记录图片",
    "description": "管理员通过 OSS 路径查看上传记录中的图片",
    "endpoint": "GET /api/admin/oss-upload-records/image",
    "userStory": "作为管理员，我希望查看上传的图片内容，以便审核图片质量"
  },
  {
    "id": "task-3",
    "title": "旋转图片",
    "description": "管理员旋转上传记录中的图片以调整方向",
    "endpoint": "POST /api/admin/oss-upload-records/{id}/rotate",
    "userStory": "作为管理员，我希望旋转方向不正确的图片，以便图片正确显示"
  },
  {
    "id": "task-4",
    "title": "移除图片",
    "description": "管理员从上传记录中移除不需要的图片",
    "endpoint": "DELETE /api/admin/oss-upload-records/{id}/images/{imageIndex}",
    "userStory": "作为管理员，我希望移除模糊或无关的图片，以便只保留有效图片"
  },
  {
    "id": "task-5",
    "title": "分配图片到错题",
    "description": "管理员将上传记录中的图片按组分配为错题条目，支持跨学科分配",
    "endpoint": "POST /api/admin/oss-upload-records/{id}/assign",
    "userStory": "作为管理员，我希望将图片分配到具体的错题条目，以便后续教师审核"
  },
  {
    "id": "task-6",
    "title": "重置上传记录状态",
    "description": "管理员重置上传记录的状态",
    "endpoint": "POST /api/admin/oss-upload-records/{id}/reset-status",
    "userStory": "作为管理员，我希望重置上传记录状态，以便重新处理异常状态的记录"
  },
  {
    "id": "task-7",
    "title": "VL 分析上传记录",
    "description": "管理员对上传记录进行视觉语言分析，自动识别图片分组和学科/年级",
    "endpoint": "POST /api/admin/oss-upload-records/{id}/analyze",
    "userStory": "作为管理员，我希望自动分析上传图片，以便快速获得分组建议"
  }
]
```
