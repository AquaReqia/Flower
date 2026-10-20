using System.Diagnostics;

namespace Flower.Agents.Telemetry;

/// <summary>
/// OpenTelemetry kaynağı. Flower tarafında
/// <c>.WithTracing(t =&gt; t.AddSource(AgentTelemetry.SourceName))</c> ile toplanır (Langfuse OTLP endpoint'ine gönderilebilir).
/// </summary>
public static class AgentTelemetry
{
    public const string SourceName = "Flower.Agents";

    public static readonly ActivitySource Source = new(SourceName);
}
