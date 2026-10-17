namespace Admin.WebApi.Services;

/// <summary>
/// Downstream HTTP service base-address options. The property defaults are the single
/// authoritative home of the former inline <c>??</c> fallbacks in Program.cs (the local
/// development addresses); every environment override arrives via Consul KV, mounted
/// configuration or environment variables.
/// </summary>
public sealed class StudentServiceOptions
{
    public const string SectionName = "StudentService";

    public string Url { get; set; } = "http://localhost:5005";
}

public sealed class MistakeServiceOptions
{
    public const string SectionName = "MistakeService";

    public string Url { get; set; } = "http://localhost:5007";
}

public sealed class HomeworkServiceOptions
{
    public const string SectionName = "HomeworkService";

    public string Url { get; set; } = "http://localhost:5009";
}
