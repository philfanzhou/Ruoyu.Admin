```json
[
  {
    "id": "task-1",
    "title": "Check legacy-data status",
    "description": "The administrator checks whether an upload record is legacy data, learning the associated mistakes' review status and the image-path situation",
    "endpoint": "GET /api/admin/oss-upload-records/legacy-check/{id}",
    "userStory": "As an administrator, I want to check whether an upload record is legacy data to decide whether cleanup is needed"
  },
  {
    "id": "task-2",
    "title": "Clean up legacy data",
    "description": "The administrator cleans up legacy upload-record data, completing the review flow, removing images, and deleting the record",
    "endpoint": "POST /api/admin/oss-upload-records/legacy-clean/{id}",
    "userStory": "As an administrator, I want to clean up leftover upload-record data to keep the system's data consistent"
  }
]
```
