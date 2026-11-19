using DevHub.Application.Connections;

namespace DevHub.Infrastructure.Connections;

internal static class ConnectionCatalog
{
    internal static ConnectionField Field(string key, string label, string type = "text", bool required = true) => new(key, label, type, required);
    internal static ConnectionChoice Choice(string key, string label, params ConnectionField[] fields) => new(key, label, fields);
    internal static ConnectionField Select(string key, string label, string[] values, bool required = true) =>
        new(key, label, "select", required, values.Select(value => new ConnectionOption(value,
            string.Join(' ', value.Split('-').Select(word => char.ToUpperInvariant(word[0]) + word[1..])))).ToArray());

    private static readonly ConnectionField Name = Field("name", "Name");
    private static readonly ConnectionField Title = Field("title", "Title");
    private static readonly ConnectionField Status = Field("status", "Status");
    private static readonly ConnectionField PreviousStatus = Field("previousStatus", "Previous status", required: false);
    private static readonly ConnectionField User = Field("userId", "Person");
    private static readonly ConnectionField Start = Field("startDate", "Start date", "date", false);
    private static readonly ConnectionField TargetDate = Field("targetDate", "Target date", "date", false);
    private static readonly ConnectionField Description = Field("description", "Description", "multiline", false);
    private static readonly ConnectionField Body = Field("body", "Markdown", "multiline");
    private static readonly ConnectionField Color = Field("color", "Color", "color", false);
    private static readonly ConnectionField Emoji = Field("emoji", "Emoji", required: false);
    private static readonly ConnectionField Parent = Field("parentId", "Parent page", required: false);
    private static readonly ConnectionField CalendarKind = Select("kind", "Entry kind", ["milestone", "meeting", "event"], false);
    private static readonly ConnectionChoice[] CalendarTimeEvents =
    [Choice("calendar-started", "Calendar entry starts", CalendarKind), Choice("calendar-ended", "Calendar entry ends", CalendarKind),
        Choice("calendar-start-offset", "Before calendar entry starts", Field("minutesBefore", "Minutes before start", "number"), CalendarKind)];
    private static readonly ConnectionField BugStatus = Select("status", "Status", ["new", "triaged", "in-progress", "resolved", "closed"]);
    private static readonly ConnectionField BugPriority = Select("priority", "Priority", ["low", "medium", "high", "critical"]);
    private static readonly ConnectionField Category = Field("categoryId", "Category", required: false);
    private static readonly ConnectionField ColumnType = new("type", "Column type", "select", true,
        new[] { "UUID", "INT", "BIGINT", "VARCHAR", "TEXT", "BOOLEAN", "DATE", "TIMESTAMP", "DECIMAL", "FLOAT", "JSON", "BLOB" }
            .Select(type => new ConnectionOption(type, type)).ToArray());
    private static readonly ConnectionChoice Exists = Choice("exists", "Node exists");
    private static readonly ConnectionChoice NameIs = Choice("name-is", "Name matches", Name);
    private static readonly ConnectionChoice TitleIs = Choice("title-is", "Title matches", Title);
    private static readonly ConnectionChoice Delete = Choice("delete", "Delete this item");
    private static readonly ConnectionChoice Rename = Choice("rename", "Rename", Name);
    private static readonly ConnectionChoice SetTitle = Choice("set-title", "Set title", Title);
    private static readonly ConnectionChoice SetDates = Choice("set-dates", "Set or clear dates", Start, TargetDate);
    private static readonly ConnectionField[] RelationshipInputs =
    [Field("fromTable", "From table"), Field("fromField", "From column ID"), Field("toTable", "To table"), Field("toField", "To column ID"),
        new("cardinality", "Cardinality", "select", true, [new("1:1", "One to one"), new("1:N", "One to many"), new("N:1", "Many to one"), new("N:M", "Many to many")]),
        new("onDelete", "On delete", "select", false, [new("RESTRICT", "Restrict"), new("CASCADE", "Cascade"), new("SET NULL", "Set null")])];
    private static readonly ConnectionField YesNoUnique = Field("unique", "Unique", "boolean", false);
    private static readonly ConnectionField CheckExpression = Field("expression", "Check expression", "multiline");
    private static readonly ConnectionField UmlType = Select("type", "Diagram type", ["class", "sequence", "use-case", "activity", "state-machine",
        "component", "deployment", "object", "package", "communication", "timing", "interaction-overview", "composite-structure", "profile"], false);
    private static readonly ConnectionField UmlSymbol = Select("symbol", "Element", ["class", "interface", "enumeration", "actor", "use-case", "boundary",
        "lifeline", "activation", "fragment", "action", "initial", "final", "flow-final", "decision", "fork", "state", "history", "deep-history", "terminate",
        "component", "node", "object", "package", "port", "timing", "stereotype", "note", "constraint", "provided-interface", "required-interface", "artifact",
        "junction", "entry-point", "exit-point", "send-signal", "accept-event", "time-event", "region", "destruction"]);
    private static readonly ConnectionField UmlRelationType = Select("symbol", "Relationship type", ["association", "directed", "generalization", "realization",
        "dependency", "aggregation", "composition", "include", "extend", "sync", "async", "reply", "create-message", "destroy-message", "note-link", "extension", "containment"]);
    private static readonly ConnectionField UmlLabel = Field("text", "Label", "multiline", false);
    private static readonly ConnectionField[] UmlEndpoints = [Field("startId", "From element"), Field("endId", "To element")];

    private static ConnectionChoice[] EventSet(string prefix, string label, ConnectionField name, bool includeAdd = false, bool renamed = true) =>
    [.. includeAdd ? new[] { Choice($"{prefix}-added", $"{label} added", name with { Required = false }) } : [],
        .. renamed ? new[] { Choice($"{prefix}-renamed", $"{label} renamed", name with { Required = false }) } : [],
        Choice($"{prefix}-updated", $"{label} updated", name with { Required = false }),
        Choice($"{prefix}-removed", $"{label} removed", name with { Required = false })];

    internal sealed record ChangeEvent(string Kind, string Label, params string[] Properties);

    internal static readonly IReadOnlyDictionary<string, ChangeEvent[]> DetailedEvents = new Dictionary<string, ChangeEvent[]>
    {
        ["erd-table"] = [new("table-constraints-changed", "Table constraints changed", "indexes", "checks")],
        ["erd-column"] = [new("column-type-changed", "Column type changed", "type", "length"), new("column-flags-changed", "Column flags changed", "primary", "unique", "nullable", "autoIncrement"),
            new("column-default-changed", "Column default changed", "defaultValue")],
        ["erd-relationship"] = [new("relationship-endpoints-changed", "Relationship endpoints changed", "fromTable", "toTable", "fromField", "toField", "fromFields", "toFields"),
            new("relationship-cardinality-changed", "Relationship cardinality changed", "cardinality", "leftEnd", "rightEnd"), new("relationship-delete-changed", "On-delete behavior changed", "onDelete")],
        ["calendar-entry"] = [new("entry-rescheduled", "Calendar entry rescheduled", "startDate", "endDate", "startTime", "endTime", "allDay"), new("entry-kind-changed", "Calendar entry kind changed", "kind")],
        ["sticky-note"] = [new("note-body-changed", "Note body changed", "body"), new("note-color-changed", "Note color changed", "color"), new("note-emoji-changed", "Note emoji changed", "emoji")],
        ["wiki-page"] = [new("page-content-changed", "Wiki page content changed", "body"), new("page-moved", "Wiki page moved", "parentId")],
        ["ui-screen"] = [new("screen-layout-changed", "Screen layout changed", "width", "height", "layout", "gap", "padding"),
            new("screen-visibility-changed", "Screen visibility changed", "hidden"), new("screen-lock-changed", "Screen lock changed", "locked")],
        ["ui-layer"] = [new("layer-content-changed", "Layer content changed", "text", "image", "control"), new("layer-moved", "Layer changed parent", "parentId"),
            new("layer-visibility-changed", "Layer visibility changed", "hidden"), new("layer-lock-changed", "Layer lock changed", "locked"),
            new("layer-interaction-changed", "Prototype interaction changed", "interaction")],
        ["uml-diagram"] = [new("diagram-type-changed", "Diagram type changed", "type")],
        ["uml-element"] = [new("element-details-changed", "Element details changed", "text", "umlDetails", "umlSymbol"),
            new("element-visibility-changed", "Element visibility changed", "hidden"), new("element-lock-changed", "Element lock changed", "locked")],
        ["uml-relationship"] = [new("uml-relationship-ends-changed", "UML relationship endpoints changed", "startShape", "endShape"),
            new("uml-relationship-details-changed", "UML relationship details changed", "umlSymbol", "text", "sourceMultiplicity", "targetMultiplicity", "sourceRole", "targetRole", "guard", "effect", "number")]
    };

    private static IEnumerable<ConnectionChoice> DetailChoices(params string[] kinds) => kinds.SelectMany(kind => DetailedEvents[kind]).Select(detail => Choice(detail.Kind, detail.Label));

    private static readonly ConnectionChoice[] TaskEvents =
    [Choice("status-changed", "Task status changed", Status with { Required = false }, PreviousStatus), Choice("task-updated", "Task edited"),
        Choice("task-completed", "Task completed"), Choice("task-reopened", "Task reopened"), Choice("task-deleted", "Task deleted"),
        Choice("assignee-added", "Assignee added", User with { Required = false }), Choice("assignee-removed", "Assignee removed", User with { Required = false })];
    private static readonly ConnectionChoice[] ColumnEvents =
    [Choice("task-entered", "Task entered column"), Choice("task-exited", "Task exited column"), Choice("column-renamed", "Column renamed"),
        Choice("column-reordered", "Column reordered"), Choice("column-removed", "Column removed"), Choice("column-became-final", "Column became final"),
        Choice("column-stopped-being-final", "Column stopped being final")];
    private static readonly ConnectionChoice[] BoardEvents =
    [Choice("board-completed", "Board completed"), Choice("board-reopened", "Board reopened"), Choice("board-dates-changed", "Board dates changed"),
        Choice("columns-changed", "Status columns changed"), Choice("column-added", "Column added"),
        Choice("task-created", "Task created", Title with { Required = false }, Status with { Required = false }), .. TaskEvents, .. ColumnEvents];
    private static readonly ConnectionChoice[] ReportEvents =
    [Choice("report-status-changed", "Report status changed", BugStatus with { Required = false }, BugStatus with { Key = "previousStatus", Label = "Previous status", Required = false }),
        Choice("report-priority-changed", "Priority changed", BugPriority with { Required = false }), Choice("report-category-changed", "Category changed", Category),
        Choice("report-notes-changed", "Internal note changed")];
    private static readonly ConnectionChoice[] CategoryEvents =
    [Choice("category-renamed", "Category renamed"), Choice("category-archived", "Category archived"), Choice("category-restored", "Category restored"),
        Choice("report-categorized", "Report categorized"), Choice("report-uncategorized", "Report uncategorized")];

    private static ConnectionChoice[] CalendarActions(bool create)
    {
        ConnectionField[] fields = [Title, Field("date", "Start date", "date"), Field("endDate", "End date", "date", false), CalendarKind, Description,
            Select("allDay", "Time mode", ["all-day", "timed"], false),
            Field("startTime", "Start time", "time", false), Field("endTime", "End time", "time", false)];
        return create ? [Choice("create-entry", "Create calendar entry", fields)] : [SetTitle, Choice("reschedule", "Reschedule", fields.Skip(1).ToArray()), Delete];
    }

    internal static readonly IReadOnlyList<ConnectionCapability> Entries =
    [
        new("todo", "todo", "TODOs", [Choice("board-created", "Board created", Name with { Required = false }), .. BoardEvents],
            [Choice("board-exists", "Board exists", Name), Exists], [Choice("create-board", "Create board", Name, Description, Start, TargetDate)]),
        new("todo-board", "todo", "TODO board", BoardEvents, [Exists, NameIs, Choice("completed", "Board is complete"), Choice("column-exists", "Column exists", Status)],
            [Choice("create-task", "Create task", Title, Status, Description, Start, TargetDate, User with { Required = false }), SetDates,
                Choice("create-column", "Add status column", Name)]),
        new("todo-column", "todo", "Status column", ColumnEvents, [Exists, NameIs, Choice("final-column", "Column is the final status")],
            [Rename, Choice("move-here", "Move task here", Field("taskId", "Task")),
                Choice("reorder-column", "Move column to position", Field("position", "Position (starting at 1)", "number")), Delete]),
        new("todo-task", "todo", "Task", TaskEvents,
            [Exists, TitleIs, Choice("status-is", "Task status is", Status), Choice("assigned-to", "Assigned to person", User), Choice("completed", "Task is complete")],
            [Choice("move-task", "Move task status", Status), SetTitle, Choice("set-description", "Set description", Description), SetDates,
                Choice("assign-task", "Assign person", User), Choice("unassign-task", "Unassign person", User), Delete]),

        new("erd", "erd", "ERD", [Choice("table-name-appears", "Table name appears", Name with { Required = false }), .. EventSet("table", "Table", Name, true),
                .. EventSet("column", "Column", Name, true), .. EventSet("relationship", "Relationship", Field("cardinality", "Cardinality"), true, false),
                Choice("dialect-changed", "SQL dialect changed"), .. DetailChoices("erd-table", "erd-column", "erd-relationship")],
            [Exists, Choice("table-exists", "Table exists", Name), Choice("table-missing", "Table is absent", Name)],
            [Choice("create-table", "Create table", Name, Field("columns", "Columns (JSON, optional)", "multiline", false)),
                Choice("remove-table", "Remove table", Field("tableId", "Table")),
                Choice("add-relationship", "Add relationship", RelationshipInputs), Choice("remove-relationship", "Remove relationship", Field("relationshipId", "Relationship")),
                Choice("set-dialect", "Set SQL dialect", new ConnectionField("dialect", "Dialect", "select", true,
                    [new("postgresql", "PostgreSQL"), new("mysql", "MySQL"), new("sqlserver", "SQL Server"), new("sqlite", "SQLite")]))]),
        new("erd-table", "erd", "ERD table", [.. EventSet("table", "Table", Name), .. EventSet("column", "Column", Name, true),
                .. EventSet("relationship", "Relationship", Field("cardinality", "Cardinality"), true, false), .. DetailChoices("erd-table", "erd-column", "erd-relationship")],
            [Exists, NameIs, Choice("column-exists", "Column exists", Name), Choice("has-primary-key", "Has primary key"),
                Choice("index-exists", "Index exists", Name), Choice("check-exists", "Check constraint exists", Name)],
            [Rename, Choice("add-column", "Add column", Name, ColumnType),
                Choice("set-index", "Add or update index", Name, Field("columns", "Column names or IDs, comma separated"), YesNoUnique),
                Choice("remove-index", "Remove index", Field("constraintId", "Index")),
                Choice("set-check", "Add or update check constraint", Name, CheckExpression),
                Choice("remove-check", "Remove check constraint", Field("checkId", "Check constraint")),
                Choice("add-relationship", "Add relationship from this table", RelationshipInputs.Skip(1).ToArray()), Delete]),
        new("erd-column", "erd", "ERD column", [.. EventSet("column", "Column", Name), .. DetailChoices("erd-column")],
            [Exists, NameIs, Choice("type-is", "Column type is", ColumnType), Choice("is-primary", "Column is a primary key")],
            [Rename, Choice("set-type", "Set column type", ColumnType),
                Choice("set-column-details", "Set column options", Field("length", "Length or precision", required: false), Field("defaultValue", "Default value", required: false),
                    Field("primary", "Primary key", "boolean", false), Field("nullable", "Nullable", "boolean", false), YesNoUnique,
                    Field("autoIncrement", "Auto increment", "boolean", false)), Delete]),
        new("erd-relationship", "erd", "ERD relationship", [.. EventSet("relationship", "Relationship", Field("cardinality", "Cardinality"), renamed: false), .. DetailChoices("erd-relationship")],
            [Exists, Choice("cardinality-is", "Cardinality is", RelationshipInputs[4])],
            [Choice("set-endpoints", "Set single-column endpoints", RelationshipInputs.Take(4).ToArray()),
                Choice("set-cardinality", "Set cardinality", RelationshipInputs[4]),
                Choice("set-delete-behavior", "Set delete behavior", RelationshipInputs[5] with { Required = true }), Delete]),

        new("calendar", "calendar", "Calendar", [.. EventSet("entry", "Calendar entry", Title, true), .. DetailChoices("calendar-entry"), .. CalendarTimeEvents, Choice("timezone-changed", "Calendar time zone changed")],
            [Exists, Choice("entry-exists", "An entry with this title exists", Title)], CalendarActions(true)),
        new("calendar-entry", "calendar", "Calendar entry", [.. EventSet("entry", "Calendar entry", Title), .. DetailChoices("calendar-entry"), .. CalendarTimeEvents],
            [Exists, TitleIs, Choice("kind-is", "Entry kind is", CalendarKind with { Required = true }),
                Choice("starts-on", "Entry starts on", Field("date", "Date", "date")), Choice("all-day", "Entry is all day")], CalendarActions(false)),

        new("bug-reports", "bug-reports", "Bug Reports", [Choice("report-submitted", "Report submitted", Select("source", "Source", ["public", "team"], false), BugPriority with { Required = false }),
                Choice("category-created", "Category created"), .. ReportEvents, .. CategoryEvents], [Exists], [Choice("create-category", "Create category", Name)]),
        new("bug-report", "bug-reports", "Bug report", ReportEvents,
            [Exists, Choice("status-is", "Report status is", BugStatus), Choice("priority-is", "Report priority is", BugPriority),
                Choice("category-is", "Report category is", Category), Choice("source-is", "Report source is", Select("source", "Source", ["public", "team"]))],
            [Choice("set-status", "Set status", BugStatus), Choice("set-priority", "Set priority", BugPriority), Choice("set-category", "Set or clear category", Category),
                Choice("set-triage-note", "Replace internal note", Field("note", "Internal note", "multiline", false)),
                Choice("append-triage-note", "Append internal note", Field("note", "Internal note", "multiline"))]),
        new("bug-category", "bug-reports", "Bug category", CategoryEvents, [Exists, NameIs, Choice("archived", "Category is archived")],
            [Rename, Choice("archive", "Archive category"), Choice("restore", "Restore category")]),

        new("notes", "notes", "Notes", [.. EventSet("note", "Note", Title, true), .. DetailChoices("sticky-note")], [Exists, Choice("note-exists", "A note with this title exists", Title)],
            [Choice("create-note", "Create note", Title, Body with { Required = false }, Color, Emoji)]),
        new("sticky-note", "notes", "Sticky note", [.. EventSet("note", "Note", Title), .. DetailChoices("sticky-note")], [Exists, TitleIs, Choice("body-contains", "Markdown contains", Field("text", "Search term")), Choice("color-is", "Color is", Color with { Required = true })],
            [SetTitle, Choice("replace-body", "Replace Markdown", Body with { Required = false }), Choice("append-body", "Append Markdown", Body),
                Choice("set-color", "Set color", Color with { Required = true }), Choice("set-emoji", "Set or clear emoji", Emoji), Delete]),

        new("wiki", "wiki", "Project Wiki", [.. EventSet("page", "Page", Title, true), .. DetailChoices("wiki-page"), Choice("home-changed", "Wiki home changed")],
            [Exists, Choice("page-exists", "A page with this title exists", Title)], [Choice("create-page", "Create wiki page", Title, Body with { Required = false }, Parent)]),
        new("wiki-page", "wiki", "Wiki page", [.. EventSet("page", "Page", Title, true), .. DetailChoices("wiki-page"),
                Choice("became-home", "Page became the wiki home"), Choice("stopped-being-home", "Page stopped being the wiki home")],
            [Exists, TitleIs, Choice("body-contains", "Markdown contains", Field("text", "Search term")), Choice("is-home", "Page is the wiki home")],
            [SetTitle, Choice("replace-body", "Replace Markdown", Body with { Required = false }), Choice("append-body", "Append Markdown", Body),
                Choice("create-subpage", "Create subpage", Title, Body with { Required = false }), Choice("move-page", "Move page", Parent), Choice("set-home", "Set as wiki home"), Delete]),

        new("ui-editor", "ui-editor", "UI/UX Editor", [.. EventSet("screen", "Screen", Name, true), .. EventSet("layer", "Layer", Name, true), .. DetailChoices("ui-screen", "ui-layer"), Choice("start-screen-changed", "Start screen changed")],
            [Exists, Choice("screen-exists", "A screen with this name exists", Name)], [Choice("create-screen", "Create screen", Name), CreateLayer()]),
        new("ui-screen", "ui-editor", "UI screen", [.. EventSet("screen", "Screen", Name), .. EventSet("layer", "Layer", Name, true), .. DetailChoices("ui-screen", "ui-layer"),
                Choice("became-start-screen", "Became the start screen"), Choice("stopped-being-start-screen", "Stopped being the start screen")],
            [Exists, NameIs, Choice("is-start", "Screen is the start screen"), Choice("hidden", "Screen is hidden"), Choice("locked", "Screen is locked")],
            [Rename, CreateLayer(), Choice("set-start", "Set as start screen"), Choice("duplicate-screen", "Duplicate screen"),
                Choice("set-layout", "Set layout", Select("layout", "Layout", ["free", "vertical", "horizontal"]), Field("gap", "Gap", "number", false), Field("padding", "Padding", "number", false)),
                .. VisibilityActions(), Delete]),
        new("ui-layer", "ui-editor", "UI layer", [.. EventSet("layer", "Layer", Name), .. DetailChoices("ui-layer")],
            [Exists, NameIs, Choice("hidden", "Layer is hidden"), Choice("locked", "Layer is locked")],
            [Rename, Choice("set-text", "Set text", Field("text", "Text", "multiline", false)), Choice("duplicate-layer", "Duplicate layer"),
                Choice("move-layer", "Move layer to screen", Field("parentId", "Destination screen", required: false)),
                Choice("set-style", "Set layer colors", Field("fill", "Fill", "color", false), Field("stroke", "Border", "color", false), Field("textColor", "Text color", "color", false)),
                Choice("configure-interaction", "Configure prototype interaction", Select("interaction", "Interaction", ["navigate", "overlay", "back", "close"]),
                    Field("targetId", "Destination screen", required: false), Select("transition", "Transition", ["instant", "fade", "slide"], false)),
                Choice("clear-interaction", "Clear prototype interaction"), .. VisibilityActions(), Delete]),

        new("uml", "uml", "UML", [.. EventSet("diagram", "Diagram", Name, true), .. EventSet("element", "Element", Field("kind", "Shape kind"), true, false),
                .. EventSet("relationship", "UML relationship", Field("kind", "Shape kind"), true, false), .. DetailChoices("uml-diagram", "uml-element", "uml-relationship")],
            [Exists, Choice("diagram-exists", "A diagram with this name exists", Name)], [Choice("create-diagram", "Create diagram", Name, UmlType)]),
        new("uml-diagram", "uml", "UML diagram", [.. EventSet("diagram", "Diagram", Name), .. EventSet("element", "Element", Field("kind", "Shape kind"), true, false),
                .. EventSet("relationship", "UML relationship", Field("kind", "Shape kind"), true, false), .. DetailChoices("uml-diagram", "uml-element", "uml-relationship")],
            [Exists, NameIs, Choice("diagram-type-is", "Diagram type is", UmlType)], [Rename, Choice("set-diagram-type", "Set or clear diagram type", UmlType),
                Choice("duplicate-diagram", "Duplicate diagram"),
                Choice("add-element", "Add UML element", UmlSymbol, UmlLabel), Choice("add-uml-relationship", "Add UML relationship", [UmlRelationType, .. UmlEndpoints, UmlLabel]), Delete]),
        new("uml-element", "uml", "UML element", [.. EventSet("element", "Element", Field("kind", "Shape kind"), renamed: false), .. DetailChoices("uml-element")],
            [Exists, Choice("hidden", "Element is hidden"), Choice("locked", "Element is locked")],
            [Choice("set-text", "Set label", UmlLabel), Choice("set-uml-details", "Set UML element details",
                    Field("name", "Name", required: false), Field("stereotype", "Stereotype", required: false),
                    Field("attributes", "Attributes", "multiline", false), Field("operations", "Operations", "multiline", false),
                    Field("entry", "Entry", "multiline", false), Field("activity", "Activity", "multiline", false), Field("exit", "Exit", "multiline", false),
                    Field("operator", "Operator", required: false), Field("operands", "Operands", "multiline", false)), .. VisibilityActions(), Delete]),
        new("uml-relationship", "uml", "UML relationship", [.. EventSet("relationship", "UML relationship", Field("kind", "Shape kind"), renamed: false), .. DetailChoices("uml-relationship")],
            [Exists, Choice("locked", "Relationship is locked")], [Choice("set-text", "Set label", UmlLabel),
                Choice("set-relation-type", "Set relationship type", UmlRelationType), Choice("reattach", "Reattach endpoints", UmlEndpoints),
                Choice("reverse-relation", "Reverse endpoints"), Choice("set-guard", "Set or clear guard", Field("guard", "Guard", "multiline", false)),
                Choice("set-effect", "Set or clear effect", Field("effect", "Effect", "multiline", false)),
                Choice("set-relationship-details", "Set roles and multiplicity", Field("sourceRole", "Source role", required: false), Field("targetRole", "Target role", required: false),
                    Field("sourceMultiplicity", "Source multiplicity", required: false), Field("targetMultiplicity", "Target multiplicity", required: false)),
                Choice("lock", "Lock"), Choice("unlock", "Unlock"), Delete]),
        .. SdlcConnections.Capabilities
    ];

    private static ConnectionChoice CreateLayer() => Choice("create-layer", "Create layer", Name,
        Select("kind", "Layer kind", ["rectangle", "ellipse", "text", "button"]), Field("text", "Text", "multiline", false));

    private static ConnectionChoice[] VisibilityActions() => [Choice("hide", "Hide"), Choice("show", "Show"), Choice("lock", "Lock"), Choice("unlock", "Unlock")];
}
