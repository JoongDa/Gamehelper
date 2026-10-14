namespace CraftHelper
{
    using System.Text.Json.Serialization;
    using GameHelper.Plugin;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class CraftHelperSettings : IPSettings
    {
        [JsonRequired]
        public string TargetConditionsJson { get; set; } = ConditionConfiguration.ExampleJson;
    }
}
