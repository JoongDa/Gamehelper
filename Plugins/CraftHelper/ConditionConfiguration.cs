namespace CraftHelper
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using CraftHelper.Matching;

    public static class ConditionConfiguration
    {
        public const string ExampleJson = """
            {
              "Logic": "And",
              "Conditions": [
                { "ModId": "Strength7", "Category": "Explicit", "Kind": "Value", "ValueIndex": 0, "Comparison": "GreaterThanOrEqual", "Minimum": 29 },
                { "ModId": "LocalCriticalMultiplier6", "Category": "Explicit", "Kind": "Value", "ValueIndex": 0, "Comparison": "GreaterThanOrEqual", "Minimum": 23 }
              ]
            }
            """;

        private static readonly JsonSerializerOptions Options = new()
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        };

        public static bool TryParse(string json, out TargetConditions? conditions, out string error)
        {
            conditions = null;
            error = string.Empty;
            try
            {
                var parsed = JsonSerializer.Deserialize<TargetConditions>(json, Options);
                var validation = ModMatcher.Evaluate(new ModSnapshot { IsValid = true }, parsed);
                if (validation.Status == MatchStatus.Invalid)
                {
                    error = $"Invalid target conditions: {validation.Reason}";
                    return false;
                }
                conditions = parsed;
                return true;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
            {
                error = $"Invalid target JSON: {ex.Message}";
                return false;
            }
        }
    }
}
