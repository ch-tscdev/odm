# Device groups

Groups are **dynamic**: their members follow from rules on device fields (name, OS family, OS name and version, primary user,
organisational unit, cost center). *All rules* or *any rule* must match; text comparisons ignore case. A group without rules
contains no devices. Membership is evaluated whenever it is needed, so new or changed devices join and leave groups by themselves.

Name, OS name and OS version are reported by the agent; primary user, organisational unit and cost center are set in ODM.

## Catch-all groups

A group marked **catch-all group** contains only devices that are in **no other group**. Typical use: assign a default
maintenance window, update policy or base software to it, so that no device is left out because nobody has put it into a group yet.

- Rules are optional. Without rules the catch-all group holds every device that is in no other group; with rules it is narrowed
  further, e.g. `OS family is Windows` for "all Windows devices not assigned anywhere else".
- New groups are taken into account automatically: their devices leave the catch-all group at once.
- Catch-all groups never exclude each other.
- **Counts for catch-all groups** (on by default) decides whether a group's devices count as assigned. Turn it off for broad
  groups such as "All Windows devices"; otherwise every Windows device would be in that group and no catch-all group could ever
  contain one.

Catch-all groups work everywhere a group is accepted: software and profile assignments, rollout subgroups, maintenance windows and
update policies.
