using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using PublicApiGenerator;
using Xunit;

namespace Line.Poc.Tests;

// Snapshot regression test for the public API surface (G4-2, design sections 8, 10).
//
// Goal: detect breaking changes as diffs in the "public API surface (public types/signatures)".
// Per design section 8, targets **only the hand-written surface** and excludes Kiota-generated code
// (namespaces containing a "Generated" segment) to avoid noise from internal generation diffs.
//
// Boundary note: top-level generated types are excluded, but **generated types exposed in the public
// signatures of hand-written types are kept** (e.g. MessagingClient.Api -> Generated.Api.MessagingApiClient). This
// is part of the public contract, so it is correct to include it in detection. A rename of a generated type name leaking
// through a signature also shifting the snapshot is intended behavior.
//
// Approval flow (ApprovalTests style):
//   - Commit the expected value to PublicApi/<Assembly>.approved.txt.
//   - PASS if the runtime value matches. On a diff, write <Assembly>.received.txt and FAIL.
//   - For an intended change, review received, apply it to approved (overwrite), and re-run.
public class PublicApiSnapshotTests
{
    // Registry of assemblies whose hand-written public surface is snapshotted.
    // Add here when a new package gains a hand-written public API (omissions are caught by the completeness test below).
    private static readonly IReadOnlyList<(string Name, Assembly Assembly)> Registered = new[]
    {
        ("Line.Core", typeof(Line.Core.Authentication.LineHosts).Assembly),
        ("Line.ChannelAccessToken", typeof(Line.ChannelAccessToken.JwtAssertionTokenSource).Assembly),
        ("Line.Messaging", typeof(Line.Messaging.MessagingClient).Assembly),
        ("Line.Liff", typeof(Line.Liff.LiffClient).Assembly),
        ("Line.Messaging.Webhook", typeof(Line.Messaging.Webhook.WebhookRequestParser).Assembly),
    };

    public static IEnumerable<object[]> RegisteredNames()
        => Registered.Select(r => new object[] { r.Name });

    [Theory]
    [MemberData(nameof(RegisteredNames))]
    public void PublicApi_Matches_Snapshot(string name)
    {
        var assembly = Registered.Single(r => r.Name == name).Assembly;
        AssertPublicApi(assembly, name);
    }

    // Completeness guard (prevents missing selections per design section 8):
    // Detects Line.* assemblies referenced by the test that "have hand-written public types but are unregistered".
    // If a new package (such as Line.Messaging.Webhook, currently with 0) gains a hand-written public API but
    // its snapshot registration is forgotten, this FAILs here to avoid a silent protection gap.
    [Fact]
    public void All_Handwritten_Line_Assemblies_Are_Registered()
    {
        var registeredNames = Registered.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);

        var referencedLineAssemblies = typeof(PublicApiSnapshotTests).Assembly
            .GetReferencedAssemblies()
            .Where(a => a.Name is not null && a.Name.StartsWith("Line.", StringComparison.Ordinal))
            .Select(Assembly.Load);

        var missing = referencedLineAssemblies
            .Where(a => HasHandwrittenPublicTypes(a))
            .Select(a => a.GetName().Name!)
            .Where(n => !registeredNames.Contains(n))
            .ToList();

        Assert.True(missing.Count == 0,
            "Some assemblies have hand-written public types but are not registered for snapshotting: " +
            string.Join(", ", missing) +
            ". Add them to PublicApiSnapshotTests.Registered and create their approved.txt.");
    }

    // Generates a snapshot targeting only the hand-written public API and compares it against approved.
    private static void AssertPublicApi(Assembly assembly, string name)
    {
        var actual = GenerateHandwrittenPublicApi(assembly);

        var dir = SnapshotDirectory();
        Directory.CreateDirectory(dir);
        var approvedPath = Path.Combine(dir, $"{name}.approved.txt");
        var receivedPath = Path.Combine(dir, $"{name}.received.txt");

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
                $"Public API surface does not match the approved snapshot: {name}\n" +
                $"If this change is intended, review {Path.GetFileName(receivedPath)} and " +
                $"apply it to {Path.GetFileName(approvedPath)}.\n" +
                $"received: {receivedPath}");
        }

        // On a match, clean up any leftover received file.
        if (File.Exists(receivedPath))
        {
            File.Delete(receivedPath);
        }
    }

    // Generates the API string from only hand-written public types, excluding Kiota-generated types.
    private static string GenerateHandwrittenPublicApi(Assembly assembly)
    {
        var handwrittenTypes = assembly.GetExportedTypes()
            .Where(t => !IsGenerated(t))
            .ToArray();

        return assembly.GeneratePublicApi(new ApiGeneratorOptions
        {
            IncludeTypes = handwrittenTypes,
            // Assembly attributes tend to be noisy across build configurations, so they are excluded.
            IncludeAssemblyAttributes = false,
        });
    }

    private static bool HasHandwrittenPublicTypes(Assembly assembly)
        => assembly.GetExportedTypes().Any(t => !IsGenerated(t));

    // A type is considered generated if its namespace contains a "Generated" segment.
    // Because it matches on a per-segment basis, it does not misclassify a hand-written namespace like
    // "Line.Core.GeneratedHelpers" (split-based matching rather than .Contains(".Generated")).
    private static bool IsGenerated(Type type)
        => (type.Namespace ?? string.Empty)
            .Split('.')
            .Contains("Generated", StringComparer.Ordinal);

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    private static string SnapshotDirectory([CallerFilePath] string thisFilePath = "")
        => Path.Combine(Path.GetDirectoryName(thisFilePath)!, "PublicApi");
}
