# Storage Audit

Storage Audit defines how the relationship between storage objects and business references is observed. It owns neither the learning business data nor the business lifecycle of storage objects.

## Language

**Audit Run**:
One complete storage-reference verification activity.
_Avoid_: Background Job, Process Log

**Audit Record**:
The observation, or record of a past disposition, that an Audit Run produces for one storage object.
_Avoid_: OSS Object, Business Record

**Orphan Object**:
A storage object that no known business record references.
_Avoid_: Deleted Object, Missing Reference

**Resolution**:
The decision made on an Audit Record — delete, ignore, or keep — and its outcome.
_Avoid_: Mistake Review, Homework Review

**Unreferenced Observation**:
An object not observed in the complete reference set at collection time; this does not prove it will remain unreferenced in the future and grants no deletion permission.
_Avoid_: Deletion Permit, Released Object
