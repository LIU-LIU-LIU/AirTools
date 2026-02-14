using System;

namespace AirTools.Models
{
    public class ToolDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "🔧";
        public Action Launch { get; set; } = () => { };
    }
}
