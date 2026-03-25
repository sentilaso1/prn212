using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkFlowPro.Services;

public static class TaskFilterSession
{
    public static string KeyForProject(Guid projectId) => $"UC16_TaskFilter_{projectId:N}";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(TaskFilterCriteria criteria)
        => JsonSerializer.Serialize(criteria, SerializerOptions);

    public static TaskFilterCriteria Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return TaskFilterCriteria.Default();

        try
        {
            return JsonSerializer.Deserialize<TaskFilterCriteria>(json, SerializerOptions)
                   ?? TaskFilterCriteria.Default();
        }
        catch
        {
            return TaskFilterCriteria.Default();
        }
    }
}
