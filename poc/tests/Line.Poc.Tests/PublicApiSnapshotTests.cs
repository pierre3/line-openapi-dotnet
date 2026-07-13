using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using PublicApiGenerator;
using Xunit;

namespace Line.Poc.Tests;

// 公開 API 表面の snapshot 回帰テスト（G4②・設計 §8, §10）。
//
// ねらい: 破壊的変更を「公開 API 表面（public 型/シグネチャ）」の差分として検知する。
// 設計 §8 の指針どおり **手書き表面のみ** を対象とし、Kiota 生成コード
// （名前空間に "Generated" セグメントを含むもの）は除外して内部生成差分のノイズを避ける。
//
// 線引きの注意: トップレベルの生成型は除外するが、**手書き型の公開シグネチャに露出する
// 生成型は残す**（例: MessagingClient.Api → Generated.Api.MessagingApiClient）。これは
// 公開契約の一部なので検知対象とするのが正しい。生成型名のリネームがシグネチャ経由で
// リークすると snapshot も振れる点は意図した挙動。
//
// 承認フロー（ApprovalTests 方式）:
//   - 期待値は PublicApi/<Assembly>.approved.txt にコミットしておく。
//   - 実行値が一致すれば PASS。差分があれば <Assembly>.received.txt を書き出して FAIL。
//   - 意図した変更なら received を確認のうえ approved へ反映（上書き）して再実行する。
public class PublicApiSnapshotTests
{
    // snapshot を取る手書き公開表面を持つアセンブリの登録簿。
    // 新パッケージに手書き公開 API を追加したらここへ足す（漏れは下の完全性テストが検知）。
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

    // 完全性ガード（設計 §8 の対象選定漏れ防止）:
    // テストが参照する Line.* アセンブリのうち「手書き public 型を持つのに未登録」を検知する。
    // 新パッケージ（現状 0 件の Line.Messaging.Webhook 等）に手書き公開 API を足したのに
    // snapshot 登録を忘れた場合、無警告の保護漏れにならないようここで FAIL させる。
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
            "手書き public 型を持つのに snapshot 未登録のアセンブリがあります: " +
            string.Join(", ", missing) +
            "。PublicApiSnapshotTests.Registered に追加し、approved.txt を作成してください。");
    }

    // 手書き公開 API のみを対象にした snapshot を生成し、approved と突き合わせる。
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
                $"承認ファイルがありません: {approvedPath}\n" +
                $"生成された表面を確認のうえ、{Path.GetFileName(receivedPath)} を " +
                $"{Path.GetFileName(approvedPath)} にリネームしてコミットしてください。");
        }

        var approved = Normalize(File.ReadAllText(approvedPath));
        if (Normalize(actual) != approved)
        {
            File.WriteAllText(receivedPath, actual);
            Assert.Fail(
                $"公開 API 表面が承認 snapshot と一致しません: {name}\n" +
                $"意図した変更なら {Path.GetFileName(receivedPath)} を確認のうえ " +
                $"{Path.GetFileName(approvedPath)} へ反映してください。\n" +
                $"received: {receivedPath}");
        }

        // 一致したら残っている received を掃除する。
        if (File.Exists(receivedPath))
        {
            File.Delete(receivedPath);
        }
    }

    // Kiota 生成型を除いた手書き公開型のみで API 文字列を生成する。
    private static string GenerateHandwrittenPublicApi(Assembly assembly)
    {
        var handwrittenTypes = assembly.GetExportedTypes()
            .Where(t => !IsGenerated(t))
            .ToArray();

        return assembly.GeneratePublicApi(new ApiGeneratorOptions
        {
            IncludeTypes = handwrittenTypes,
            // アセンブリ属性はビルド構成でノイズになりやすいため除外する。
            IncludeAssemblyAttributes = false,
        });
    }

    private static bool HasHandwrittenPublicTypes(Assembly assembly)
        => assembly.GetExportedTypes().Any(t => !IsGenerated(t));

    // 名前空間に "Generated" セグメントが含まれれば生成型とみなす。
    // セグメント単位で判定するため "Line.Core.GeneratedHelpers" のような手書き名前空間を
    // 誤検知しない（.Contains(".Generated") ではなく分割一致）。
    private static bool IsGenerated(Type type)
        => (type.Namespace ?? string.Empty)
            .Split('.')
            .Contains("Generated", StringComparer.Ordinal);

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    private static string SnapshotDirectory([CallerFilePath] string thisFilePath = "")
        => Path.Combine(Path.GetDirectoryName(thisFilePath)!, "PublicApi");
}
