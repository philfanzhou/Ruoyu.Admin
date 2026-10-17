```json
[
  {
    "id": "task-1",
    "title": "Browse the mistake list",
    "description": "The administrator browses the mistake-record list page by page, filtered by student, subject, grade, and review status",
    "endpoint": "GET /api/admin/mistakes",
    "userStory": "As an administrator, I want to view the mistake-record list to get an overview of the mistake data"
  },
  {
    "id": "task-2",
    "title": "View mistake details",
    "description": "The administrator views a single mistake's detailed information, including image regions and bounding boxes",
    "endpoint": "GET /api/admin/mistakes/{id}",
    "userStory": "As an administrator, I want to view a mistake's detailed information to understand its specific content"
  },
  {
    "id": "task-3",
    "title": "View mistakes by upload record",
    "description": "The administrator views all the mistakes associated with an upload record ID",
    "endpoint": "GET /api/admin/mistakes/by-upload/{uploadId}",
    "userStory": "As an administrator, I want to view the associated mistakes by upload record to trace the image-assignment results"
  },
  {
    "id": "task-4",
    "title": "Edit mistake information",
    "description": "The administrator edits a mistake's student, subject, and grade information",
    "endpoint": "PUT /api/admin/mistakes/{id}",
    "userStory": "As an administrator, I want to change a mistake's classification information to correct a wrong classification"
  },
  {
    "id": "task-5",
    "title": "Migrate mistake images",
    "description": "Migrate the mistake's images from the uploads/ path to the mistakes/ path",
    "endpoint": "POST /api/admin/mistakes/migrate-images/{id}",
    "userStory": "As an administrator, I want to migrate images to the correct storage path to keep data storage consistent"
  }
]
```
