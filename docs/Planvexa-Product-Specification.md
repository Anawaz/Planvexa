# Planvexa — Product Specification

## 1. Product Overview

**Planvexa** is a flexible work management and collaboration platform designed to help individuals and teams organize, plan, execute, communicate, track, and review work from one application.

Planvexa is intended to be industry-independent. It should work equally well for:

- Personal life management
- Software and IT projects
- Construction and engineering projects
- Education and academic work
- Healthcare and service organizations
- Consulting and professional services
- Manufacturing and operations
- Marketing and creative work
- Freelancers and independent professionals
- Small businesses
- Large teams and organizations
- Any structured work, project, process, or activity

The product combines task management, collaboration, planning, documents, communication, time tracking, reporting, automation, integrations, and optional AI capabilities.

The product should feel powerful for advanced teams while remaining understandable for an individual managing personal work.

---

## 2. Product Goals

Planvexa should provide a single system for organizing and managing work without forcing every user into the same workflow.

Primary goals:

1. Allow one User to participate in multiple independent Workspaces.
2. Keep each Workspace completely isolated in membership, permissions, settings, and data.
3. Support flexible work structures through Spaces, Folders, Lists, Tasks, and Subtasks.
4. Support both simple personal workflows and complex organizational processes.
5. Provide strong permissions and sharing without making everyday use difficult.
6. Combine planning, execution, communication, tracking, and reporting.
7. Provide realtime collaboration across tasks, documents, chat, and other shared resources.
8. Support time tracking and resource planning as first-class capabilities.
9. Provide customizable views for different ways of working.
10. Support workflow automation and external integrations.
11. Provide optional AI capabilities without making AI mandatory.
12. Remain responsive, accessible, secure, and usable on desktop, tablet, and mobile.

---

## 3. Product Principles

Planvexa should follow these product principles:

### 3.1 Flexible, not industry-specific

The product must not assume that a Workspace represents only a company or that a Space represents only a department.

A Workspace may represent:

- A company
- A family
- A personal environment
- A school
- A consultancy
- A construction business
- A client environment
- A volunteer group
- Any independent body of work

A Space may represent:

- A project
- A department
- A client
- A product
- A construction site
- A class
- A course
- A life area
- A business function
- A team
- Any major grouping of work

### 3.2 Simple by default, powerful when needed

A new user should be able to create a Workspace, Space, List, and Task without configuring advanced features.

Advanced capabilities should become available when needed without making basic workflows complicated.

### 3.3 Permission-safe by default

Users should see only the resources they are allowed to access.

Permissions must apply consistently across normal pages, search, reports, exports, realtime events, automations, integrations, public links, and AI.

### 3.4 Real data and real workflows

Production features must operate through actual application data and persistent APIs.

Normal product experiences must not depend on mock data, placeholder controls, or raw database identifiers.

### 3.5 Consistent experience

Features should share common interaction patterns for:

- Create
- Edit
- Save
- Delete
- Restore
- Move
- Copy
- Share
- Search
- Filter
- Sort
- Assign
- Comment
- Attach
- Navigate

---

## 4. Core Product Hierarchy

Planvexa uses the following hierarchy:

```text
Global User Identity
└── Workspace Memberships
    └── Workspace
        └── Space
            ├── Folderless List
            └── Folder
                └── Subfolder
                    └── List
                        └── Task
                            └── Nested Subtask
```

There is no additional Organization or Tenant level above Workspace.

---

## 5. User

A **User** is the global identity of a person using Planvexa.

A User should:

- Sign in once.
- Maintain one global profile.
- Belong to multiple Workspaces.
- Hold a different role in each Workspace.
- Switch between Workspaces without signing in again.
- Maintain personal preferences.
- Receive notifications from permitted Workspaces.
- Maintain recent items and favorites.
- Access personal views such as My Work and Inbox.

A User's role or permissions in one Workspace must never automatically provide access to another Workspace.

---

## 6. Workspace

A **Workspace** is the independent top-level product environment.

Each Workspace contains its own:

- Members
- Guests
- Teams
- Roles
- Permissions
- Settings
- Spaces
- Folders
- Lists
- Tasks
- Workflows
- Custom Fields
- Documents
- Chat
- Whiteboards
- Clips
- Forms
- Time tracking
- Planning
- Goals
- Portfolios
- Dashboards
- Reports
- Automations
- Integrations
- AI configuration
- Files
- Audit history
- Security settings

### 6.1 Workspace creation

A User should be able to create a Workspace by providing basic information such as:

- Workspace name
- Icon or image
- Optional description
- Timezone
- Week-start preference

The creator becomes the initial Workspace Owner.

### 6.2 Workspace switching

The Workspace switcher should:

- Show all accessible Workspaces.
- Show useful identity information such as name and icon.
- Allow fast switching.
- Remember the last active Workspace where appropriate.
- Prevent data from the previous Workspace from appearing after a switch.
- Redirect safely when the currently open resource does not exist in the selected Workspace.

### 6.3 Workspace settings

Workspace settings should include areas such as:

- General settings
- Members
- Guests
- Teams
- Roles and permissions
- Security
- Notifications
- Working calendar
- Custom Fields
- Integrations
- Automations
- AI settings
- Data and exports
- Audit history
- Branding where supported

### 6.4 Workspace deletion

An Owner may permanently delete a Workspace. Deletion is irreversible — there is no archive or
restore path — so it requires both the Owner role and retyping the Workspace slug, and it can only be
performed from inside the Workspace being deleted.

Deletion removes every row the Workspace owns. This is enforced in the database rather than in
application code: every table carrying a `workspace_id` has a foreign key to `tenancy.workspaces`
with `ON DELETE CASCADE`, so the operation is a single `DELETE` and any future workspace-owned table
inherits the behaviour by declaring its own key the same way. Stored files under the Workspace's blob
prefix are swept afterwards on a best-effort basis; the database rows are already committed by then,
so a sweep failure is logged rather than surfaced.

Two things deliberately outlive the Workspace: `audit.audit_events` and `platform.outbox_messages`
are excluded from the cascade. The `workspace.deleted` audit record is written and committed before
the delete, so the deletion remains provable afterwards; the deleted Workspace's outbox rows are
cleared explicitly so nothing is published for a Workspace that no longer exists.

---

## 7. Space

A **Space** is a major organizational unit within a Workspace.

Spaces may represent departments, projects, clients, products, construction sites, business functions, classes, courses, personal life areas, operational teams, or other work groupings.

Each Space should support:

- Name
- Description
- Icon
- Color
- Owner
- Status workflow
- Privacy
- Sharing
- Default views
- Templates
- Custom Fields
- Ordering
- Favorites
- Archive
- Restore

A Space may contain:

- Folderless Lists
- Folders
- Subfolders
- Lists
- Related views
- Related documents
- Related chat channels
- Related dashboards
- Other Space-scoped resources

---

## 8. Folders and Subfolders

Folders provide optional structural grouping inside Spaces.

Folders should support:

- Name
- Description
- Nested Subfolders
- Safe nesting
- Cycle prevention
- Drag-and-drop ordering
- Move
- Copy
- Duplicate
- Archive
- Restore
- Delete
- Privacy
- Sharing
- Permission inheritance
- Templates
- Inherited Custom Fields

Folders should never be mandatory.

A List may exist directly inside a Space without a Folder.

---

## 9. Lists

A **List** is a container for related Tasks.

A List may exist:

- Directly inside a Space
- Inside a Folder
- Inside a Subfolder

Lists should support:

- Name
- Description
- Status workflow
- Views
- Templates
- Privacy
- Sharing
- Custom Fields
- Task ordering
- Move
- Copy
- Duplicate
- Archive
- Restore
- Delete

Lists should provide a clear overview of their Tasks and support switching between applicable views.

---

## 10. Task Management

Tasks are the primary actionable work items in Planvexa.

### 10.1 Task properties

Tasks should support:

- Title
- Rich-text description
- Status
- Priority
- Start date
- Due date
- Due time
- Assignees
- Team assignees
- Watchers
- Tags
- Milestone status
- Estimates
- Remaining time
- Custom Fields
- Attachments
- Dependencies
- Relationships
- Checklists
- Comments
- Activity history
- Time tracking
- Reminders
- Recurrence
- Templates
- Custom task type
- Custom task ID

### 10.2 Subtasks

Tasks may contain nested Subtasks.

Subtasks should support the same major capabilities as Tasks where applicable.

The hierarchy should remain navigable and visually understandable even when Subtasks are nested multiple levels.

### 10.3 Task assignment

Tasks should support:

- One or more User assignees
- Team assignees
- Removing assignees
- Filtering by assignee
- Assignment notifications

### 10.4 Task relationships

Tasks should support:

- Blocking
- Blocked by
- Depends on
- Related to
- Generic configurable relationships

Relationships must respect Workspace boundaries and permissions.

### 10.5 Multiple-List membership

Where enabled, a Task should be able to appear in multiple Lists without creating duplicate Task records.

Changes should remain synchronized because all appearances represent the same Task.

### 10.6 Task operations

Users with appropriate permissions should be able to:

- Create
- Edit
- Move
- Copy
- Duplicate
- Merge
- Archive
- Trash
- Restore
- Permanently delete
- Bulk assign
- Bulk change status
- Bulk change dates
- Bulk change fields
- Bulk archive

Important Task changes should appear in activity history.

---

## 11. Statuses and Workflows

Planvexa should support customizable Task workflows.

Workflow capabilities should include:

- Custom statuses
- Status names
- Status colors
- Status categories
- Initial status
- Active statuses
- Completed statuses
- Closed statuses
- Ordering
- Allowed transitions
- Optional transition restrictions
- Reusable workflow templates

Workflows may be configured at appropriate hierarchy levels and inherited where applicable.

### 11.1 Resolution: Workspace defaults, optional Space overrides

A status scheme is either **workspace-level** (`space_id IS NULL`) or a **Space override**
(`space_id` set). Exactly one workspace-level scheme is the workspace default, and only a
workspace-level scheme can be the default.

A Space resolves its **effective scheme** as `spaces.status_scheme_id ?? the workspace default`. A
Space with no override inherits, and every Space that inherits shares the same scheme — so editing an
inherited scheme changes them all, which is why the per-Space screen is read-only until the Space
customizes. A List still stores its own `status_scheme_id` and that remains the single resolution
point for every task operation; what changed is only the fallback used when a new List is created,
which is now the Space's effective scheme rather than the workspace default.

Customizing a Space clones its effective scheme and moves each task to the matching status in the
clone, so the operation is lossless. Customizing *from a template* has no such correspondence and
moves every task in the Space to the new scheme's default status. Tasks that are merely cross-listed
into the Space keep their primary List's scheme and are not moved.

### 11.2 Removing a status always names a replacement

`tasks.status_id` has no foreign key to `statuses`, so a removed status would silently orphan its
tasks. Every operation that would strand tasks therefore requires the caller to name a replacement
status, and the tasks are moved to it through the normal status-change path (so completion flags and
status-changed events stay correct). This applies to removing a single status and to reverting a
Space to the workspace default, which requires a mapping for every Space status that still holds
tasks. A workflow may never drop below one status.

Known ceiling: saved-view filter JSON and Automations rule config can still reference a removed
status id. Those references are not rewritten — a stale filter simply matches nothing.

---

## 12. Priorities

Default priorities should provide a simple ordered system such as:

- Urgent
- High
- Normal
- Low
- No priority

Workspaces may support customization where appropriate.

Priority should be available in:

- Tasks
- Filters
- Sorting
- Views
- Reports
- Automations
- Search
- Notifications

---

## 13. Custom Fields

Planvexa should support extensible Custom Fields.

Supported field types should include:

- Text
- Long text
- Number
- Currency
- Boolean
- Date
- DateTime
- Dropdown
- Multi-select
- User
- Team
- Email
- Phone
- URL
- Location
- Rating
- Progress
- Formula
- Relationship
- Rollup

Custom Fields should support:

- Name
- Description
- Required state
- Default value
- Validation
- Options where applicable
- Reordering
- Visibility
- Permissions
- Inheritance
- Filtering
- Sorting
- Grouping
- Reporting

### 13.1 Formula fields

Formula fields should support:

- References to supported fields
- Calculations
- Validation
- Dependency tracking
- Recalculation
- Cycle prevention
- Clear error states

### 13.2 Relationship fields

Relationship fields should link Tasks or supported resources.

### 13.3 Rollup fields

Rollups should aggregate values from related records.

Supported operations may include:

- Count
- Sum
- Average
- Minimum
- Maximum
- Progress calculation

---

## 14. Views

Users should be able to visualize work in different ways without changing the underlying Tasks.

Planvexa should provide:

- My Work
- Inbox
- List View
- Table View
- Board View
- Calendar View
- Timeline View
- Gantt View
- Workload View
- Team View
- Activity View
- Map View
- Sprint View
- Dashboard View

### 14.1 Common view capabilities

Views should support:

- Private views
- Shared views
- Saved configurations
- Favorites
- Templates
- Filters
- Nested filter groups
- Sorting
- Grouping
- Nested grouping
- Configurable columns
- Column resizing
- Conditional formatting
- Inline editing
- Drag-and-drop
- Pagination
- Virtualization
- Responsive layouts
- Personal view preferences

### 14.2 Board View

Board View should support:

- Configurable grouping
- Task cards
- Drag between groups
- Quick status changes
- Assignee display
- Priority display
- Due dates
- Selected Custom Fields
- Quick Task creation

### 14.3 Calendar View

Calendar should support:

- Month
- Week
- Day
- Drag-and-drop rescheduling
- Filters
- Unscheduled Tasks
- Assignee filtering
- Team filtering
- Date-range navigation

### 14.4 Gantt View

Gantt should support:

- Task hierarchy
- Start and due dates
- Dependencies
- Milestones
- Progress
- Drag resizing
- Drag rescheduling
- Dependency creation
- Baselines
- Critical path
- Zoom levels
- Working calendars

### 14.5 Workload View

Workload should help users understand capacity and assignment levels based on:

- Assignees
- Teams
- Estimates
- Scheduled work
- Capacity
- Time period
- Availability
- Leave
- Work schedules

---

## 15. My Work

My Work should provide each User with a personal cross-Workspace or Workspace-filtered view of relevant work.

It should show information such as:

- Assigned Tasks
- Upcoming Tasks
- Overdue Tasks
- Recently assigned Tasks
- Tasks created by the User
- Tasks watched by the User
- Personal priorities
- Calendar items
- Reminders
- Time-sensitive work

Users should be able to filter and organize My Work without modifying the underlying shared structure.

---

## 16. Inbox and Notifications

The Inbox should provide a centralized notification experience.

Notifications may include:

- Assignment
- Mention
- Comment
- Reply
- Reaction
- Status change
- Due-date reminder
- Dependency change
- Invitation
- Approval request
- Automation result
- Document mention
- Chat mention
- Goal update

Users should be able to:

- Mark notifications read/unread
- Open the related resource
- Filter notifications
- Clear or archive notifications
- Configure notification preferences

Notification channels may include:

- In-app
- Email
- Browser push
- Mobile push
- Daily digest
- Weekly digest

---

## 17. Search and Navigation

Planvexa should provide fast navigation across large Workspaces.

### 17.1 Global search

Search should cover permitted content including:

- Tasks
- Subtasks
- Lists
- Folders
- Spaces
- Documents
- Comments
- Chat messages
- Members
- Teams
- Dashboards
- Forms
- Goals
- Whiteboards
- Clips

Search results must respect permissions.

### 17.2 Search capabilities

Search should support:

- Keyword search
- Workspace filtering
- Resource-type filtering
- Recent searches
- Useful result previews
- Direct navigation
- Optional semantic search

### 17.3 Command palette

A command palette should provide quick access to:

- Navigation
- Search
- Create Task
- Create List
- Create Space
- Switch Workspace
- Recently opened items
- Common actions

### 17.4 Favorites and recents

Users should be able to favorite commonly used resources and quickly access recent items.

---

## 18. Comments and Activity

Tasks and other collaborative resources should support comments.

Comment capabilities should include:

- Rich text
- Threaded replies
- Mentions
- Reactions
- Attachments
- Edit
- Delete where permitted
- Timestamps
- Realtime updates

Activity history should record important events such as:

- Task created
- Status changed
- Assignee changed
- Dates changed
- Priority changed
- Custom Field changed
- Attachment added
- Dependency changed
- Task moved
- Time logged

Activity should clearly distinguish system-generated events from human comments.

---

## 19. Realtime Collaboration

Planvexa should update shared content without requiring manual refresh.

Realtime capabilities should include:

- Task updates
- Comments
- Notifications
- Chat messages
- Presence
- Typing indicators
- Document collaboration
- Whiteboard collaboration
- Relevant dashboard refreshes

Realtime events must respect Workspace and resource permissions.

---

## 20. Chat

Planvexa should include integrated communication.

Chat should support:

- Workspace channels
- Space channels
- List-linked channels
- Task-linked discussions
- Private channels
- Direct messages
- Group direct messages
- Threads
- Mentions
- Reactions
- Attachments
- Read state
- Unread counts
- Search
- Presence
- Typing indicators

Linked channels should inherit or integrate with the permissions of their related resources where appropriate.

---

## 21. Documents and Wikis

Planvexa should provide collaborative Documents and Wikis.

Capabilities should include:

- Rich-text editing
- Realtime collaborative editing
- Presence
- Live cursors
- Headings
- Tables
- Lists
- Checklists
- Code blocks
- Callouts
- Embeds
- Images
- Files
- Task references
- Mentions
- Comments
- Document hierarchy
- Wiki organization
- Templates
- Autosave
- Search
- Version history
- Restore
- Export
- Public sharing
- Private sharing

Documents should be linkable to Tasks, Spaces, Lists, Goals, and other supported resources.

---

## 22. Whiteboards

Whiteboards should support visual planning and collaboration.

Capabilities should include:

- Free drawing
- Shapes
- Connectors
- Sticky notes
- Text
- Images
- Task links
- Document links
- Selection
- Move
- Resize
- Zoom
- Collaborative editing
- Presence
- Templates
- Export
- Permissions
- Recovery/version support where practical

---

## 23. Clips

Clips should support asynchronous visual communication.

Capabilities should include:

- Screen recording
- Audio recording
- Video recording
- File upload
- Playback
- Comments
- Transcription
- Searchable transcripts
- Task linking
- Document linking
- Permission-aware access

---

## 24. Forms

Forms should allow structured information collection and automatic work creation.

Capabilities should include:

- Drag-and-drop builder
- Public Forms
- Authenticated Forms
- Required fields
- Conditional logic
- Custom Field mapping
- File uploads
- Branding
- Spam protection
- Submission limits
- Confirmation pages
- Automatic Task creation
- Assignment rules
- Status routing
- Priority routing
- Tag routing
- Due-date routing
- Team routing
- List routing
- Automation triggers
- Submission history
- CSV export
- Excel export

---

## 25. Time Tracking

Time tracking should be a first-class product capability.

### 25.1 Timers

Support:

- Global timer
- Task timer
- Start
- Pause
- Resume
- Stop
- Timer recovery after refresh
- Timer recovery after browser closure
- Clear active-timer visibility

The server should remain authoritative for active timer state.

### 25.2 Manual time entries

Users should be able to enter:

- Date
- Start time
- End time
- Duration
- Description
- Task
- Tags
- Billable/non-billable status

### 25.3 Estimates

Tasks should support:

- Time estimates
- Remaining time
- Estimate versus actual comparison

### 25.4 Rates

Where enabled, support:

- Member rate
- Project rate
- Client rate
- Cost rate

### 25.5 Timesheets

Timesheets should support:

- Daily view
- Weekly view
- Submission
- Approval
- Rejection
- Reopening
- Locking
- Edit reasons after approval
- Missing-time reminders

### 25.6 Time reporting

Time reports should support:

- User
- Team
- Task
- List
- Space
- Date range
- Billable status
- Tags
- Estimate versus actual
- Utilization
- Overtime
- Cost
- Profitability

---

## 26. Work Schedules, Capacity, and Leave

Workspace planning should support:

- Working days
- Daily working hours
- Holidays
- Leave
- Availability
- Capacity
- Utilization
- Scheduled effort
- Logged effort
- Over-allocation warnings

These capabilities should integrate with Workload, planning, time tracking, and reporting.

---

## 27. Sprints and Agile Planning

For teams using agile workflows, Planvexa should support:

- Backlog
- Sprint creation
- Sprint dates
- Sprint goals
- Task assignment to Sprint
- Story points
- Sprint status
- Burndown
- Burnup
- Velocity
- Carry-over handling
- Completed versus planned work

Sprint features should remain optional so non-agile users are not forced into this model.

---

## 28. Goals and OKRs

Goals should support strategic and personal objective tracking.

Capabilities should include:

- Goal folders
- Goal owner
- Goal period
- Description
- Status
- Numeric targets
- Monetary targets
- Percentage targets
- Task-based targets
- Key Results
- Comments
- Linked Tasks
- Linked projects
- Progress history
- Permissions
- Dashboards
- Reporting

Goals should support both organizational and personal use.

---

## 29. Portfolios

Portfolios should provide a higher-level view across multiple projects or work areas.

Capabilities should include:

- Included projects or Lists
- Owners
- Health
- Status
- Progress
- Dates
- Milestones
- Risks
- Budgets
- Custom Fields
- Summary reporting
- Permission-aware sharing

---

## 30. Dashboards

Dashboards should provide configurable visual summaries.

Widgets may include:

- Tasks by status
- Tasks by assignee
- Tasks by priority
- Overdue Tasks
- Completed Tasks
- Created versus completed
- Time logged
- Estimate versus actual
- Billable time
- Workload
- Goal progress
- Sprint progress
- Portfolio health
- Numeric calculations
- Tables
- Charts

Dashboards should support:

- Filters
- Date ranges
- Sharing
- Permissions
- Refresh
- Drill-down
- Templates
- Personal dashboards
- Shared dashboards

---

## 31. Reporting

Reporting should provide both operational and management visibility.

Reports should support:

- Saved reports
- Filters
- Grouping
- Sorting
- Date ranges
- Drill-down
- Scheduled reports
- CSV export
- Excel export
- PDF export
- Custom formulas

Report areas should include:

- Tasks
- Productivity
- Time
- Workload
- Status
- Goals
- Sprints
- Portfolios
- Utilization
- Profitability
- Custom Fields

All reports must respect permissions.

---

## 32. Automations

Planvexa should provide configurable workflow automation.

The model is:

```text
Trigger → Conditions → Actions
```

### 32.1 Triggers

Examples:

- Task created
- Task updated
- Status changed
- Assignee changed
- Due date reached
- Date condition
- Scheduled trigger
- SLA trigger
- Form submitted
- Comment created
- Time entry created

### 32.2 Conditions

Conditions should support:

- Status
- Priority
- Assignee
- Team
- Tags
- Dates
- Custom Fields
- Relationships
- Nested AND/OR groups

### 32.3 Actions

Actions should support:

- Assign User
- Assign Team
- Change status
- Change priority
- Add/remove tag
- Set dates
- Update Custom Field
- Add comment
- Send notification
- Send email
- Send webhook
- Run integration action

### 32.4 Automation management

Users should be able to:

- Create
- Edit
- Enable
- Disable
- Duplicate
- Test
- View execution history
- View errors
- Retry where permitted

Automations should include protection against accidental loops and duplicate execution.

---

## 33. Integrations

Planvexa should support external services through integrations.

Target integrations include:

- Google Calendar
- Microsoft Outlook Calendar
- Slack
- Microsoft Teams
- GitHub
- GitLab
- Google Drive
- OneDrive
- SharePoint
- Email
- n8n
- Generic webhooks

Integrations should provide:

- Connection status
- Permissions/scopes
- Configuration
- Health information
- Error information
- Disconnect option

---

## 34. Developer Platform

Planvexa should provide developer-facing integration capabilities.

These should include:

- Versioned REST API
- OpenAPI documentation
- Personal API tokens
- OAuth applications
- Scoped access
- Webhooks
- Signed webhook delivery
- Idempotency support
- Rate limits
- Integration logs

Developer capabilities must respect the same permissions as the normal product.

---

## 35. Import and Migration

Users should be able to import work from common systems and structured files.

Target import sources include:

- ClickUp
- Jira
- Trello
- Asana
- CSV
- Excel

Imports should support:

- Validation
- Field mapping
- User mapping
- Status mapping
- Hierarchy mapping
- Preview
- Progress
- Error reporting
- Resume where possible
- Duplicate prevention
- Audit/history

---

## 36. Optional AI Capabilities

AI should remain optional.

Potential AI capabilities include:

- Task summaries
- Comment summaries
- Document summaries
- Chat summaries
- Suggested Subtasks
- Suggested assignees
- Suggested priorities
- Suggested due dates
- Risk detection
- Dependency suggestions
- Duplicate Task detection
- Meeting-note extraction
- Status-report generation
- Time-entry description assistance
- Automation generation
- Semantic search
- Workspace question answering

AI must:

- Respect Workspace permissions.
- Respect resource permissions.
- Never expose inaccessible data.
- Make suggestions rather than silently making high-impact changes.
- Clearly indicate AI-generated content where appropriate.
- Support configurable models/providers.
- Allow AI to be completely disabled.

---

## 37. Files and Attachments

Planvexa should provide reliable file handling across Tasks, Documents, Chat, Forms, Whiteboards, and other supported resources.

Capabilities should include:

- Upload
- Download
- Drag-and-drop
- File previews
- Images
- PDFs
- Video
- Metadata
- File-size validation
- File-type validation
- Secure access
- Permission-aware download
- File removal
- Orphan cleanup

---

## 38. Public Sharing

Supported resources may be shared through public links where enabled.

Public sharing should support:

- Enable/disable
- Expiration
- Revocation
- Password protection
- Permission level
- Access restrictions
- Access auditing

Public links must not expose related private resources implicitly.

---

## 39. Offline and PWA Experience

Planvexa should support installable Progressive Web App behavior.

Capabilities should include:

- Installable application
- Offline reading
- Offline Task creation
- Offline Task editing
- Offline comments
- Offline time entries
- Queued changes
- Automatic synchronization
- Conflict detection
- Conflict resolution
- Workspace-isolated offline data
- Push notifications

Offline mode should clearly indicate:

- Connectivity state
- Pending changes
- Synchronization state
- Conflicts
- Failed actions

---

## 40. User Profile and Preferences

Users should be able to manage:

- Name
- Profile image
- Language
- Timezone
- Date format
- Time format
- Week start
- Notification preferences
- Theme
- Accessibility preferences
- Personal productivity preferences

Where appropriate, Workspace-level settings may override or complement User preferences.

---

## 41. Themes and Appearance

Planvexa should support:

- Light mode
- Dark mode
- System theme
- Consistent design tokens
- Responsive layouts
- Clear visual hierarchy
- Accessible contrast

Workspace branding may optionally support:

- Workspace icon
- Workspace color
- Workspace logo
- Other limited appearance settings

---

## 42. Accessibility

Target accessibility:

**WCAG 2.2 AA**

Product requirements include:

- Full keyboard navigation
- Visible focus
- Screen-reader labels
- Semantic HTML
- Accessible forms
- Accessible dialogs
- Accessible tables
- Accessible menus
- Drag-and-drop alternatives
- Appropriate color contrast
- Reduced-motion support
- Error identification
- Responsive text/layout behavior

---

## 43. Internationalization and Timezones

Planvexa should support:

- Translatable interface text
- User locale
- Locale-aware dates
- Locale-aware numbers
- Locale-aware currencies
- IANA timezones
- Configurable week start
- Workspace working calendars
- Right-to-left readiness

Dates and times displayed to a User should reflect appropriate timezone context.

---

## 44. Security and Privacy Requirements

From the User's perspective, the product must provide strong protection for Workspace and personal data.

Requirements include:

- Secure authentication
- MFA support
- Session management
- Workspace isolation
- Resource-level authorization
- Private resources
- Secure invitations
- Audit history
- Secure public links
- Secure file access
- Rate limiting
- Security-sensitive action logging
- User data export
- User data deletion
- Workspace data export
- Data-retention controls where applicable

The product should fail safely rather than accidentally reveal inaccessible information.

---

## 45. Audit and Activity History

Planvexa should provide auditability appropriate to both teams and advanced organizations.

Auditable events should include areas such as:

- Membership changes
- Role changes
- Permission changes
- Workspace settings
- Security settings
- Public sharing
- Task changes
- Time-entry changes
- Approval actions
- Automation changes
- Integration changes
- Data exports

Audit information should include where appropriate:

- Actor
- Action
- Resource
- Timestamp
- Before/after information
- Source
- Related Workspace

---

## 46. Performance and Scale

The product should remain responsive as Workspaces grow.

Requirements include:

- Fast common navigation
- Server-side pagination where necessary
- Large-list virtualization
- Efficient filtering
- Efficient sorting
- Efficient search
- Responsive Task editing
- Efficient realtime updates
- Background processing for long-running actions
- Useful loading indicators
- Graceful handling of large datasets

Initial target service levels may include:

- Common reads: p95 below 250 ms
- Common writes: p95 below 400 ms
- Realtime updates: typically below one second
- Search: p95 below 750 ms
- Timer actions: confirmation below 500 ms

These are product targets to validate through testing.

---

## 47. Responsive Experience

Planvexa should provide usable experiences on:

- Desktop
- Laptop
- Tablet
- Mobile browsers

Responsive behavior should preserve access to essential functionality rather than merely shrinking desktop layouts.

Core mobile-friendly actions should include:

- View Tasks
- Create Task
- Edit Task
- Change status
- Assign
- Comment
- Track time
- Search
- View notifications
- Navigate Workspace hierarchy

---

## 48. Empty, Loading, and Error States

Every major feature should provide intentional states for:

- Loading
- Empty data
- First-time use
- Validation failure
- Access denied
- Not found
- Conflict
- Rate limited
- Server failure
- Offline
- Retry

Error messages should explain what happened and, where possible, what the User can do next.

---

## 49. Product Quality Requirements

A feature should not be considered complete simply because a page exists.

A complete feature should have:

- Working navigation
- Persisted data
- Functional create/read/update/delete behavior where applicable
- Permissions
- Validation
- Loading state
- Empty state
- Error state
- Responsive layout
- Accessibility
- Realtime behavior where applicable
- Search/report integration where applicable
- Audit/activity integration where applicable
- Automated test coverage appropriate to its importance

Normal Users should never have to enter raw UUIDs or database identifiers.

---

## 50. Product Originality

Planvexa should maintain its own product identity while implementing standard work-management concepts.

Planvexa should have its own:

- Navigation structure
- Design system
- Layouts
- Icons
- Typography
- Product copy
- Onboarding
- Templates
- Interaction details
- Documentation
- Example data

Generic product concepts such as Workspace, Space, Folder, List, Task, Board, Calendar, Dashboard, Form, Comment, and Time Tracking may be used normally.

---

## 51. Key User Journeys

The following journeys should work smoothly from end to end.

### 51.1 New User onboarding

```text
Register / Sign in
→ Create or join Workspace
→ Create or enter Space
→ Create/open List
→ Create first Task
→ Assign / schedule / track
```

### 51.2 Multiple Workspace User

```text
Sign in
→ Open Workspace A
→ Work normally
→ Switch to Workspace B
→ Previous Workspace data disappears
→ Continue with Workspace B permissions
```

### 51.3 Project setup

```text
Create Space
→ Create Folder structure if needed
→ Create Lists
→ Configure workflow
→ Add Custom Fields
→ Create/import Tasks
→ Assign team
→ Save useful Views
```

### 51.4 Task execution

```text
Open Task
→ Review description and fields
→ Comment / collaborate
→ Add files
→ Track time
→ Complete checklist/Subtasks
→ Change status
→ Complete Task
```

### 51.5 Team planning

```text
Open workload/planning view
→ Review capacity
→ Identify unassigned/overloaded work
→ Reassign or reschedule
→ Review estimates
→ Confirm plan
```

### 51.6 Manager reporting

```text
Open Dashboard / Report
→ Filter scope and period
→ Review status, workload, time, goals, or risks
→ Drill into underlying work
→ Export/share when permitted
```

### 51.7 Personal life management

```text
Open personal Workspace
→ Organize Spaces by life area
→ Create Lists for routines/projects/goals
→ Create Tasks
→ Schedule and prioritize
→ Track progress
→ Review through My Work, Calendar, and Goals
```

---

## 52. Product Completion Definition

Planvexa is considered product-complete against this specification when:

- Users can belong to multiple Workspaces.
- Workspace switching is safe and seamless.
- Roles and permissions are independent per Workspace.
- Workspace data is fully isolated.
- Spaces, Folders, Subfolders, Lists, Tasks, and nested Subtasks work end to end.
- Members, Guests, Teams, invitations, and ownership management work.
- Resource-level sharing and permissions work consistently.
- Task management capabilities operate with persisted data.
- Status workflows work.
- Custom Fields, including advanced field types, work.
- Major Views work.
- Search and navigation work across major resources.
- My Work and Inbox are functional.
- Comments, activity, notifications, and realtime collaboration work.
- Chat works.
- Documents and Wikis work.
- Whiteboards work.
- Clips work.
- Forms work.
- Time tracking and timesheets work.
- Work schedules, leave, capacity, and workload work.
- Sprints and agile planning work where enabled.
- Goals and OKRs work.
- Portfolios work.
- Dashboards and reporting work.
- Automations work.
- Integrations and developer APIs work.
- Import tools work.
- Optional AI capabilities work when enabled.
- File management works securely.
- Public sharing works securely.
- PWA and offline workflows work.
- User preferences, themes, localization, and timezones work.
- Accessibility requirements are met.
- Major workflows perform reliably at expected scale.
- Major pages work on desktop, tablet, and mobile.
- Loading, empty, validation, permission, error, and offline states are handled.
- No normal feature depends on mock data or placeholder behavior.
- No normal User workflow requires raw UUID input.
- Browser console and network behavior are clean during normal journeys.
- Product documentation reflects implemented behavior.

---

## 53. Canonical Product Model

```text
User
│
├── Profile and Preferences
├── My Work
├── Inbox
│
└── Workspace Memberships
    │
    ├── Workspace A
    │   │
    │   ├── Members
    │   ├── Guests
    │   ├── Teams
    │   ├── Roles and Permissions
    │   ├── Workspace Settings
    │   │
    │   ├── Space
    │   │   │
    │   │   ├── Folderless List
    │   │   │   └── Task
    │   │   │       └── Nested Subtask
    │   │   │
    │   │   └── Folder
    │   │       └── Subfolder
    │   │           └── List
    │   │               └── Task
    │   │                   └── Nested Subtask
    │   │
    │   ├── Views
    │   ├── Search
    │   ├── Comments and Activity
    │   ├── Notifications
    │   ├── Chat
    │   ├── Documents and Wikis
    │   ├── Whiteboards
    │   ├── Clips
    │   ├── Forms
    │   ├── Time Tracking
    │   ├── Workload and Capacity
    │   ├── Sprints
    │   ├── Goals and OKRs
    │   ├── Portfolios
    │   ├── Dashboards and Reports
    │   ├── Automations
    │   ├── Integrations and API
    │   ├── Optional AI
    │   ├── Files
    │   ├── Public Sharing
    │   └── Audit History
    │
    └── Workspace B
        └── Independent members, permissions, settings, and data
```

This document defines the intended **product behavior, capabilities, user experience, and completion criteria** for Planvexa.
