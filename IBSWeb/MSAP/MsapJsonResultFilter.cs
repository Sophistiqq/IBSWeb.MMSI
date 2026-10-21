using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IBSWeb.MSAP
{
    public class MsapJsonResultFilter : IResultFilter
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            Converters = { new MsapDecimalJsonConverter() }
        };

        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.ActionDescriptor.RouteValues.TryGetValue("area", out var area)
                && area is "MSAP" or "MSAPAdmin" or "MSAPSuperAdmin"
                && context.Result is JsonResult result)
            {
                result.SerializerSettings = Options;
            }
        }

        public void OnResultExecuted(ResultExecutedContext context)
        {
        }
    }

    internal class MsapDecimalJsonConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetDecimal();
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(Math.Round(value, 2, MidpointRounding.AwayFromZero));
        }
    }
}
