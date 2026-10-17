# Assistant Management — Conventions

- Keep the proxy pattern and frontend interactions fully symmetric with teacher management
- Assistant-student association management is done uniformly in the "Linked accounts" dialog on the student management page, not on the assistant management page
- The Admin Portal does not touch the assistant database directly; everything goes through the proxy middleware
