using System.Text.Json;
using DevHub.Infrastructure.ProjectTools;

internal static class NotesChecks
{
    public static void Run()
    {
        var note = new {id = "note-1", title = "Meeting notes", body = "Keep the next release focused.", color = "#cce5ce", updatedAt = "2026-10-20T00:00:00.000Z"};
        bool Valid(object[] notes) => ToolDocumentValidation.Valid("notes", JsonSerializer.SerializeToElement(new {notes}));
        Check(Valid([]) && Valid([note]), "Empty and populated notes documents accepted");
        Check(!Valid([note, note]), "Duplicate note identities rejected");
        Check(!Valid([note with {title = " "}]), "Blank note titles rejected");
        Check(!Valid([note with {body = new string('a', 10001)}]), "Oversized note bodies rejected");
        Check(!Valid([note with {color = "red"}]), "Invalid note colors rejected");
        Check(!Valid([note with {updatedAt = "not-a-date"}]), "Invalid note dates rejected");
        Check(!Valid(["invalid"]), "Malformed note entries rejected");
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }
}
