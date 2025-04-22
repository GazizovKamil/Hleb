using Hleb.Helpers;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hleb.Dto
{
    public class BuildMap
    {
        public DateTime date { get; set; }
    }

    public class ImportExcel
    {
        public IFormFile file { get; set; }

        public DateTime date { get; set; }
    }
}