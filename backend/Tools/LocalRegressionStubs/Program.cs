using Ruoyu.Admin.LocalRegressionStubs;

// Local regression stand-ins for the three downstream reference sources consumed by the
// OSS audit (Student :5005 / Mistake :5007 / Homework :5009). One process, three loopback
// ports, read-only fixtures. See docs/development/LocalRegressionEnv.md.
//
//   dotnet run --project backend/Tools/LocalRegressionStubs -- seed   [--connection-string ...] [--fixtures ...] [--oss-root ...]
//   dotnet run --project backend/Tools/LocalRegressionStubs -- serve [--fixtures ...] [--mistake-outage]
//
// The stubs implement exactly the three read-only queries the audit aggregates
// (OssAuditWorker / OssAuditController) and nothing else. They hold no credentials and are
// bound to loopback only. Stopping the whole process makes all three sources unreachable,
// which is how the regression drives the abort / 502 branches.

var mode = args.Length == 0 || args[0] == "serve" ? "serve" : args[0];
var options = ParseOptions(args.Skip(args.Length > 0 && args[0] is "seed" or "serve" ? 1 : 0));

switch (mode)
{
    case "seed":
        return await RegressionSeeder.RunAsync(options);
    case "serve":
        RunServer(options);
        return 0;
    default:
        Console.Error.WriteLine($"未知模式：{mode}（可用：seed / serve）");
        return 2;
}

static Dictionary<string, string?> ParseOptions(IEnumerable<string> args)
{
    var options = new Dictionary<string, string?>(StringComparer.Ordinal);
    var list = args.ToList();
    for (var i = 0; i < list.Count; i++)
    {
        if (!list[i].StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }
        var name = list[i][2..];
        var value = i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal)
            ? list[++i]
            : null;
        options[name] = value ?? "true";
    }
    return options;
}

static void RunServer(Dictionary<string, string?> options)
{
    var fixturesPath = options.TryGetValue("fixtures", out var fixturesOption)
        && fixturesOption is not null && fixturesOption != "true"
            ? fixturesOption
            : Environment.GetEnvironmentVariable("LOCAL_REGRESSION_FIXTURES") ?? "data/regression/fixtures.json";
    var mistakeOutage = (options.TryGetValue("mistake-outage", out var outage) && outage == "true")
        || Environment.GetEnvironmentVariable("MISTAKE_STUB_OUTAGE") == "1";

    var fixtures = RegressionFixtures.ReadFile(fixturesPath);
    Console.WriteLine(
        $"本地替身启动：Student=:{StubServer.DefaultStudentPort} Mistake=:{StubServer.DefaultMistakePort} " +
        $"Homework=:{StubServer.DefaultHomeworkPort}，夹具={Path.GetFullPath(fixturesPath)}，" +
        $"Mistake 模拟不可达={(mistakeOutage ? "开" : "关")}");

    StubServer.Build(fixtures, mistakeOutage).Run();
}
