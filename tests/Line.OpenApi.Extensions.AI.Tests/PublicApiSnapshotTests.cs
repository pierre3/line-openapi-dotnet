using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using PublicApiGenerator;
using Xunit;

namespace Line.OpenApi.Extensions.AI.Tests;

// Public API surface snapshot for Line.OpenApi.Extensions.AI (design section 6: the AI package is a
// published, consumer-facing surface, so it is snapshotted separately from the src/** packages).
//
// The shared-source DTOs (Line.OpenApi.Tools.Services.*) are internal, so they do not appear here —
// the surface is only Line.OpenApi.Extensions.AI.*.
//
// Approval flow: on a diff the runtime surface is written to <name>.received.txt and the test FAILs;
// for an intended change, review it and overwrite <name>.approved.txt.
public class PublicApiSnapshotTests
{
    private const string Name = "Line.OpenApi.Extensions.AI";

    [Fact]
    public void PublicApi_Matches_Snapshot()
    {
        var assembly = typeof(LineMessagingAiTools).Assembly;
        var actual = assembly.GeneratePublicApi(new ApiGeneratorOptions { IncludeAssemblyAttributes = false });

        var dir = SnapshotDirectory();
        Directory.CreateDirectory(dir);
        var approvedPath = Path.Combine(dir, $"{Name}.approved.txt");
        var receivedPath = Path.Combine(dir, $"{Name}.received.txt");

        if (!File.Exists(approvedPath))
        {
            File.WriteAllText(receivedPath, actual);
            Assert.Fail(
                $"Approved file is missing: {approvedPath}\n" +
                $"Review the generated surface, then rename {Path.GetFileName(receivedPath)} to " +
                $"{Path.GetFileName(approvedPath)} and commit it.");
        }

        var approved = Normalize(File.ReadAllText(approvedPath));
        if (Normalize(actual) != approved)
        {
            File.WriteAllText(receivedPath, actual);
            Assert.Fail(
                $"Public API surface does not match the approved snapshot: {Name}\n" +
                $"If this change is intended, apply {Path.GetFileName(receivedPath)} to " +
                $"{Path.GetFileName(approvedPath)}.\nreceived: {receivedPath}");
        }

        if (File.Exists(receivedPath)) File.Delete(receivedPath);
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    private static string SnapshotDirectory([CallerFilePath] string thisFilePath = "")
        => Path.Combine(Path.GetDirectoryName(thisFilePath)!, "PublicApi");
}
