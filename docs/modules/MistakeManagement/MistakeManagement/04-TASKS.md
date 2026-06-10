```json
[
  {
    "id": "task-1",
    "title": "浏览错题列表",
    "description": "管理员分页浏览错题记录列表，按学生、学科、年级、审核状态筛选",
    "endpoint": "GET /api/admin/mistakes",
    "userStory": "作为管理员，我希望查看错题记录列表，以便了解错题数据概况"
  },
  {
    "id": "task-2",
    "title": "查看错题详情",
    "description": "管理员查看单条错题的详细信息，包括图片区域和边界框",
    "endpoint": "GET /api/admin/mistakes/{id}",
    "userStory": "作为管理员，我希望查看错题的详细信息，以便了解错题的具体内容"
  },
  {
    "id": "task-3",
    "title": "按上传记录查看错题",
    "description": "管理员根据上传记录 ID 查看关联的所有错题",
    "endpoint": "GET /api/admin/mistakes/by-upload/{uploadId}",
    "userStory": "作为管理员，我希望按上传记录查看关联错题，以便追踪图片分配结果"
  },
  {
    "id": "task-4",
    "title": "编辑错题信息",
    "description": "管理员编辑错题的学生、学科、年级信息",
    "endpoint": "PUT /api/admin/mistakes/{id}",
    "userStory": "作为管理员，我希望修改错题的分类信息，以便纠正错误分类"
  },
  {
    "id": "task-5",
    "title": "迁移错题图片",
    "description": "将错题图片从 uploads/ 路径迁移到 mistakes/ 路径",
    "endpoint": "POST /api/admin/mistakes/migrate-images/{id}",
    "userStory": "作为管理员，我希望将图片迁移到正确的存储路径，以便保持数据存储一致性"
  }
]
```
