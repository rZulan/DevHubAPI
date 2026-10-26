using System.Text.Json;
using DevHub.Infrastructure.ProjectTools;

internal static class WikiChecks
{
    public static void Run()
    {
        var page = new {id = "home", slug = "home", title = "Home", body = "# Welcome\n\n[Setup](setup)", message = "Initial page", authorId = "member", updatedAt = "2026-10-26T05:00:00.000Z", history = Array.Empty<object>()};
        bool Valid(object[] pages, string? homePageId = "home") => ToolDocumentValidation.Valid("wiki", JsonSerializer.SerializeToElement(new {pages, homePageId}));
        Check(Valid([], null) && Valid([page]), "Empty and populated wikis accepted");
        Check(!Valid([page, page]), "Duplicate wiki identities rejected");
        Check(!Valid([page, page with {id = "other"}]), "Duplicate wiki slugs rejected");
        Check(!Valid([page, page with {id = "other", slug = "other", title = " home "}]), "Case-insensitive duplicate wiki titles rejected");
        Check(!Valid([page], "missing") && !Valid([page], null), "Wiki home must reference an existing page");
        Check(!Valid([page with {slug = "../home"}]), "Unsafe wiki slugs rejected");
        Check(!Valid([page with {title = " "}]) && !Valid([page with {body = new string('x', 100001)}]), "Blank titles and oversized wiki bodies rejected");
        Check(!Valid([page with {updatedAt = "not-a-date"}]), "Invalid wiki revision dates rejected");
        var revision = new {id = "version-1", title = "Original", body = "Earlier content", message = "First page", authorId = "member", updatedAt = page.updatedAt};
        Check(Valid([page with {history = [revision]}]), "Wiki history and restore content accepted");
        Check(!Valid([page with {history = [revision, revision]}]), "Duplicate wiki revision identities rejected");
        Check(!Valid([page with {history = ["bad revision"]}]), "Malformed wiki history rejected");
        Check(!Valid(Enumerable.Range(0, 101).Select(i => (object)(page with {id = $"p{i}", slug = $"page-{i}", title = $"Page {i}"})).ToArray(), "p0"), "Wiki page limit enforced");
        Check(!Valid([page with {history = Enumerable.Range(0, 21).Select(i => (object)(revision with {id = $"v{i}"})).ToArray()}]), "Wiki history limit enforced");
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }
}
