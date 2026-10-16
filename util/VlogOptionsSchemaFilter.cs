// 檔案路徑：System\util\VlogOptionsSchemaFilter.cs
using System.Collections.Generic;
using System.Linq;
using backend.Services;
using backend.ViewModels;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace backend.util
{
    /// <summary>
    /// Swagger：遊客 VLOG CreateFinal 的 bgm_mood、video_orientation 在 Schema 列出可選值與預設值，
    /// user_memory_text 標上字數上限。欄位型別仍是字串、送法不變；可選值跟 Options API、送出前的檢查用同一份清單（VlogAiGateway）
    /// </summary>
    public class VlogOptionsSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type != typeof(VisitorVlogCreateFinalRequest)) return;

            SetEnum(schema, "bgm_mood", VlogAiGateway.BgmMoodOptions);
            SetEnum(schema, "video_orientation", VlogAiGateway.VideoOrientationOptions);
            if (schema.Properties.TryGetValue("user_memory_text", out OpenApiSchema memory))
                memory.MaxLength = VlogAiGateway.MaxUserMemoryTextLength;
        }

        private static void SetEnum(OpenApiSchema schema, string name, IEnumerable<VlogOptionItem> options)
        {
            if (!schema.Properties.TryGetValue(name, out OpenApiSchema property)) return;
            property.Enum = options.Select(o => (IOpenApiAny)new OpenApiString(o.value)).ToList();
            property.Default = new OpenApiString(options.First(o => o.is_default).value);
        }
    }
}
