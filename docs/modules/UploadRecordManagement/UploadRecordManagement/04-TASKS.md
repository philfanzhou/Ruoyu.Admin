```json
[
  {
    "id": "task-1",
    "title": "Browse the upload-record list",
    "description": "The administrator browses all students' upload records page by page, filtered by status and student",
    "endpoint": "GET /api/admin/oss-upload-records",
    "userStory": "As an administrator, I want to view the students' upload-record list to understand the upload situation"
  },
  {
    "id": "task-2",
    "title": "View upload-record images",
    "description": "The administrator views the images of an upload record through their OSS paths",
    "endpoint": "GET /api/admin/oss-upload-records/image",
    "userStory": "As an administrator, I want to view the uploaded image content to review image quality"
  },
  {
    "id": "task-3",
    "title": "Rotate images",
    "description": "The administrator rotates images in an upload record to fix their orientation",
    "endpoint": "POST /api/admin/oss-upload-records/{id}/rotate",
    "userStory": "As an administrator, I want to rotate wrongly-oriented images so they display correctly"
  },
  {
    "id": "task-4",
    "title": "Remove images",
    "description": "The administrator removes unneeded images from an upload record",
    "endpoint": "DELETE /api/admin/oss-upload-records/{id}/images/{imageIndex}",
    "userStory": "As an administrator, I want to remove blurry or irrelevant images so only valid ones remain"
  },
  {
    "id": "task-5",
    "title": "Assign images to mistakes",
    "description": "The administrator assigns the images of an upload record to mistake items in groups, supporting cross-subject assignment",
    "endpoint": "POST /api/admin/oss-upload-records/{id}/assign",
    "userStory": "As an administrator, I want to assign images to concrete mistake items so teachers can review them later"
  },
  {
    "id": "task-6",
    "title": "Reset an upload record's status",
    "description": "The administrator resets an upload record's status",
    "endpoint": "POST /api/admin/oss-upload-records/{id}/reset-status",
    "userStory": "As an administrator, I want to reset upload-record status so records in an abnormal state can be reprocessed"
  },
  {
    "id": "task-7",
    "title": "VL-analyze an upload record",
    "description": "The administrator runs vision-language analysis on an upload record, automatically recognizing image groupings and subject/grade",
    "endpoint": "POST /api/admin/oss-upload-records/{id}/analyze",
    "userStory": "As an administrator, I want to analyze the uploaded images automatically to get grouping suggestions quickly"
  }
]
```
