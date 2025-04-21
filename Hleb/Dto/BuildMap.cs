using Hleb.Helpers;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hleb.Dto
{
    public class BuildMap
    {
        [JsonPropertyName("date")]
        [DataType(DataType.Date)]
        [JsonConverter(typeof(DateOnlyJsonConverter))]
        public DateOnly date { get; set; }
    }

    public class ImportExcel
    {
        public IFormFile file { get; set; }

        public DateTime date { get; set; }
    }
}