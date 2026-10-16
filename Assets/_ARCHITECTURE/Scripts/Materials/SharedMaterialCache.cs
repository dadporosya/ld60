// DEAD FILE — safe to delete, along with PerfLogs/_to_delete/.
//
// This held the material interning cache used by MasterMaterialController and OutlineController.
// Both were reverted to per-object materials: sharing a material between objects is only safe
// while nothing writes per-object values, and those controllers exist to write per-object values.
// Forking a shared material back into a private one on demand turned out not to be reliable, and
// units picked up each other's pixel resolution.
//
// The file is emptied rather than removed because this session cannot delete files on the device.
