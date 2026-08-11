-- Planvexa DbUp script 0073_AddStatusTransitionRestrictions.sql
-- Optional Status workflow transition restrictions (spec section 11: "Allowed transitions" /
-- "Optional transition restrictions", enforced by the backend, not only the UI -- see
-- StatusScheme.SetAllowedTransitions/CanTransition and WorkItemService's status-change guard).
--
-- allowed_next_status_ids_json is a JSON array of status ids this status may transition to, mirroring
-- the existing config_json/allowed_next_status_ids_json-style "serialized list on a text column"
-- convention already used by SavedView.ConfigJson and DashboardWidget.ConfigJson in this schema. NULL
-- (the default, and every existing row after this migration) means unrestricted -- a status with no
-- configured restriction may move to any other status in its scheme, so no existing workflow changes
-- behavior until a workspace opts in.
--
-- ADD COLUMN IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9).

ALTER TABLE work.statuses ADD COLUMN IF NOT EXISTS allowed_next_status_ids_json text;
